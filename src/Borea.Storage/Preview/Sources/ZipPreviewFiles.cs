using System.IO.Compression;
using System.Security.Cryptography;
using Borea.Storage.Mods;

namespace Borea.Storage.Preview.Sources;

/// <summary>
/// The files of a mod or vehicle archive, read in place from the zip below the root that an install would take.
/// Nothing is extracted to disk. The archive stays open read-only until the source is disposed.
/// </summary>
/// <remarks>
/// <para>
/// The entries are looked up by their normalized name with the root stripped and
/// <see cref="StringComparer.OrdinalIgnoreCase"/>, because an install extracts them to a folder where the game finds
/// a file in any case and with either separator (FileReference.ModPath). When two entries differ only by case, the
/// later one wins, as it does when <see cref="ModArchive.Extract"/> writes both to a Windows folder. An entry whose
/// name is rooted or holds a ".." segment is left out.
/// </para>
/// <para>
/// Every read counts the decompressed bytes it really gets and stops at the cap of the entry and at the total of the
/// archive, because the length an archive declares is the author's claim. <see cref="TryOpen"/> decompresses an entry
/// into the shared <see cref="PreviewEntryCache"/> once and then serves it from there.
/// </para>
/// <para>
/// A <see cref="ZipArchive"/> is not safe for parallel reads, so every access to the archive, including a read on a
/// stream from <see cref="TryOpenHead"/>, takes one lock. After <see cref="Dispose"/> the members and the head
/// streams throw <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
internal sealed class ZipPreviewFiles : IPreviewFiles
{
    private readonly Lock _gate = new();
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly PreviewEntryCache _cache;
    private readonly PreviewFileCaps _caps;
    private readonly long _totalCap;
    private readonly string _sha256;
    private long _decompressedBytes;
    private bool _disposed;

    /// <param name="root">The folder inside the archive that becomes the mod folder, or null for the archive root.</param>
    /// <param name="archiveSha256">The SHA-256 of the archive when the caller knows it, such as the download hash of the release. Else the source hashes the file once.</param>
    /// <param name="installSizeBytes">The unpacked size the release states, which sets the total cap.</param>
    /// <exception cref="IOException">The file cannot be opened.</exception>
    /// <exception cref="InvalidDataException">The file is not a zip archive.</exception>
    public ZipPreviewFiles(string zipPath, string? root, string? archiveSha256, long? installSizeBytes, PreviewEntryCache cache, PreviewFileCaps? caps = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        _cache = cache;
        _caps = caps ?? PreviewFileCaps.Default;
        _totalCap = PreviewFileCaps.ArchiveTotal(installSizeBytes);

        var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            _sha256 = string.IsNullOrWhiteSpace(archiveSha256)
                ? Convert.ToHexStringLower(SHA256.HashData(stream))
                : archiveSha256.Trim().ToLowerInvariant();
            stream.Position = 0;
            _archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        try
        {
            Index(root is null ? null : PreviewPath.Normalize(root));
        }
        catch
        {
            _archive.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the archive under the root an install takes: the root the release states, else the one that
    /// <see cref="ModArchive.DeriveRoot(string, IReadOnlyList{string})"/> finds for the markers.
    /// </summary>
    public static ZipPreviewFiles Open(
        string zipPath,
        string? declaredRoot,
        IReadOnlyList<string> rootMarkers,
        string? archiveSha256,
        long? installSizeBytes,
        PreviewEntryCache cache)
    {
        var root = declaredRoot ?? ModArchive.DeriveRoot(zipPath, rootMarkers);
        return new ZipPreviewFiles(zipPath, root, archiveSha256, installSizeBytes, cache);
    }

    /// <summary>The decompressed bytes this source has read from the archive so far, counted as they arrive.</summary>
    public long DecompressedBytes => Interlocked.Read(ref _decompressedBytes);

    public bool Exists(string path)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return Find(path) is not null;
        }
    }

    public string? Describe(string path)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return Find(path) is { } found ? Key(found.Entry) : null;
        }
    }

    public Stream? TryOpenHead(string path)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (Find(path) is not { } found)
                return null;

            if (_cache.TryGet(Key(found.Entry), out var cached))
                return Wrap(cached);

            try
            {
                return new GuardedStream(this, new BoundedCountingStream(found.Entry.Open(), _caps.For(found.Name), Count));
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
    }

    public Stream? TryOpen(string path)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (Find(path) is not { } found)
                return null;

            var key = Key(found.Entry);
            if (_cache.TryGet(key, out var cached))
                return Wrap(cached);

            if (Decompress(found) is not { } bytes)
                return null;

            _cache.Add(key, bytes);
            return Wrap(bytes);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _archive.Dispose();
        }
    }

    private static MemoryStream Wrap(byte[] bytes) => new(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);

    /// <summary>
    /// Reads the whole entry under the lock, which the caller holds. Returns null when the entry is longer than its
    /// cap or than what is left of the archive total, or when its data is broken.
    /// </summary>
    private byte[]? Decompress((string Name, ZipArchiveEntry Entry) found)
    {
        var cap = _caps.For(found.Name);
        var capacity = (int)Math.Clamp(found.Entry.Length, 0, Math.Min(cap, Array.MaxLength));
        try
        {
            using var bounded = new BoundedCountingStream(found.Entry.Open(), cap, Count);
            using var guarded = new GuardedStream(this, bounded);
            using var buffer = new MemoryStream(capacity);
            guarded.CopyTo(buffer);
            if (bounded.LimitExceeded || guarded.TotalExceeded)
                return null;

            return buffer.TryGetBuffer(out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
                ? segment.Array
                : buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private void Index(string? prefix)
    {
        foreach (var entry in _archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;

            if (PreviewPath.Normalize(entry.FullName) is not { } name)
                continue;

            if (prefix is not null)
            {
                if (!name.StartsWith(prefix + "/", StringComparison.Ordinal))
                    continue;

                name = name[(prefix.Length + 1)..];
            }

            _entries[name] = entry;
        }
    }

    private (string Name, ZipArchiveEntry Entry)? Find(string path)
    {
        if (PreviewPath.Normalize(path) is not { } name || !_entries.TryGetValue(name, out var entry))
            return null;

        return (name, entry);
    }

    private string Key(ZipArchiveEntry entry) => $"zip:{_sha256}:{entry.FullName}";

    private void Count(int bytes) => Interlocked.Add(ref _decompressedBytes, bytes);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Takes the archive lock for every read, refuses a read after the source is disposed, and ends the stream when
    /// the archive has given its total.
    /// </summary>
    private sealed class GuardedStream(ZipPreviewFiles owner, Stream inner) : Stream
    {
        public bool TotalExceeded { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            lock (owner._gate)
            {
                owner.ThrowIfDisposed();
                if (TotalExceeded || buffer.IsEmpty)
                    return 0;

                // At the total, one more byte tells an entry that ends there from one that goes on.
                var remaining = owner._totalCap - owner.DecompressedBytes;
                if (remaining <= 0)
                {
                    Span<byte> probe = stackalloc byte[1];
                    if (inner.Read(probe) > 0)
                        TotalExceeded = true;

                    return 0;
                }

                return inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (owner._gate)
                    inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
