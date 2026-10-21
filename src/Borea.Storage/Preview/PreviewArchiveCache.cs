using System.Collections.Concurrent;
using System.Security.Cryptography;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Planning;
using Borea.Storage.Files;
using Borea.Storage.Mods;

namespace Borea.Storage.Preview;

/// <summary>
/// The release archives that the vehicle preview fetched before an install, one file archives/&lt;sha256&gt;.zip
/// below <see cref="IGamePathProvider.GetPreviewCacheFolder"/>, with the hash in lowercase hex.
/// </summary>
/// <remarks>
/// A file gets its final name only after its bytes matched the release hash, so a file with that name is
/// always complete and verified. The download goes to a .part file with a unique name and then moves to the
/// final name without overwrite, so a crash leaves only a .part file and two processes that fetch the same
/// archive cannot damage each other. The cache deletes files only in <see cref="Sweep"/>, which the App
/// starts once. Nothing in the constructor reads or writes the disk.
/// </remarks>
public sealed class PreviewArchiveCache
{
    /// <summary>The total size of the archives that <see cref="Sweep"/> keeps.</summary>
    public const long DefaultCapacityBytes = 1L << 30;

    /// <summary>A .part file older than this belongs to a fetch that stopped, because no fetch takes this long.</summary>
    public static readonly TimeSpan StalePartAge = TimeSpan.FromHours(24);

    private const string ArchivesFolderName = "archives";
    private const string ArchiveExtension = ".zip";
    private const string PartExtension = ".part";
    private const int HashLength = 64;

    private readonly string _cacheFolder;
    private readonly string _archivesFolder;
    private readonly IBoreaLog _log;
    private readonly long _capacityBytes;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, int> _pins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchGates = new(StringComparer.Ordinal);

    public PreviewArchiveCache(IGamePathProvider paths, IBoreaLog log, long capacityBytes = DefaultCapacityBytes, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);

        _log = log ?? throw new ArgumentNullException(nameof(log));
        _cacheFolder = Path.GetFullPath(paths.GetPreviewCacheFolder());
        _archivesFolder = Path.Combine(_cacheFolder, ArchivesFolderName);
        _capacityBytes = capacityBytes;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// The path of the cached archive with this hash, or null when the cache does not have it. A hit sets the
    /// last write time of the file to now, which is the use time that the eviction reads. Pin the hash first
    /// when the file must stay until it was read, because a sweep can delete an unpinned file at any time.
    /// </summary>
    public string? TryGet(string sha256)
    {
        var path = ArchivePath(Key(sha256));
        if (!File.Exists(path))
            return null;

        try
        {
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file is there, so a failed touch only makes it look older to the eviction.
            _log.Write($"Could not mark the preview archive {Path.GetFileName(path)} as used.", exception);
        }

        return path;
    }

    /// <summary>
    /// Returns the cached archive of the release, and downloads it first when the cache does not have it.
    /// Before a download the volume must hold the archive size plus the margin of <see cref="DriveInstallSpaceCheck"/>.
    /// The bytes are hashed again after the download, and a mismatch keeps no file. Fetches of one hash in this
    /// process run one after another, so the second one finds the file of the first.
    /// </summary>
    /// <exception cref="ArgumentException">The release has no SHA-256, so nothing could verify a cached file.</exception>
    /// <exception cref="InsufficientDiskSpaceException">The volume of the cache is too full.</exception>
    /// <exception cref="DownloadFailedException">The download failed or gave other bytes.</exception>
    public async Task<string> FetchAsync(
        ModVersionMetadata release,
        IModDownloader downloader,
        IFreeSpaceProbe freeSpace,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(freeSpace);

        if (release.Download.Sha256 is not { } sha256)
            throw new ArgumentException($"The release {release.ModId} {release.Version} has no SHA-256, so the preview does not keep its archive.", nameof(release));

        var key = Key(sha256);
        var gate = _fetchGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGet(key) is { } cached)
                return cached;

            EnsureSpace(release, freeSpace);
            Directory.CreateDirectory(_archivesFolder);

            var target = ArchivePath(key);
            var part = Path.Combine(_archivesFolder, $"{key}.{Guid.NewGuid():N}{PartExtension}");
            try
            {
                await downloader.DownloadAsync(release, part, progress, cancellationToken).ConfigureAwait(false);

                // The downloader verifies too, but the file name is the promise that the bytes match, so the cache checks it itself.
                var actual = await HashAsync(part, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, key, StringComparison.Ordinal))
                    throw new DownloadFailedException($"The archive of {release.ModId} {release.Version} has the SHA-256 {actual}, and the release names {key}.");

                try
                {
                    File.Move(part, target, overwrite: false);
                }
                catch (IOException) when (File.Exists(target))
                {
                    // Another process moved the same verified bytes into place first.
                }

                return target;
            }
            finally
            {
                TryDelete(part);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Deletes the cached archive with this hash, for a file whose bytes no longer match its name. A file that
    /// another reader holds open stays, and the failure is logged. Returns true when no such file is left.
    /// </summary>
    public bool Remove(string sha256)
    {
        var path = ArchivePath(Key(sha256));
        return !File.Exists(path) || TryDelete(path);
    }

    /// <summary>
    /// Keeps the archive with this hash through every <see cref="Sweep"/> until the returned handle is disposed.
    /// Pins count, so two holders of one hash keep it until both let go.
    /// </summary>
    public IDisposable Pin(string sha256)
    {
        var key = Key(sha256);
        lock (_pins)
            _pins[key] = _pins.GetValueOrDefault(key) + 1;

        return new PinHandle(this, key);
    }

    /// <summary>
    /// Deletes the .part files older than <see cref="StalePartAge"/>, the folders of the other cache versions,
    /// and the least recently used archives until the rest fits in the capacity. A pinned archive and a file
    /// that another process holds open stay. A failure is logged and never thrown, because a cache that cannot
    /// clean up must not stop the App. Returns how many files and folders it deleted.
    /// </summary>
    public int Sweep()
    {
        var deleted = DeleteOtherVersions();

        var archives = new List<FileInfo>();
        try
        {
            var folder = new DirectoryInfo(_archivesFolder);
            if (!folder.Exists)
                return deleted;

            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var file in folder.EnumerateFiles())
            {
                if (file.Name.EndsWith(PartExtension, StringComparison.Ordinal))
                {
                    if (now - file.LastWriteTimeUtc > StalePartAge && TryDelete(file.FullName))
                        deleted++;
                }
                else if (IsArchiveName(file.Name))
                {
                    archives.Add(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Write("Could not read the preview archive folder.", exception);
            return deleted;
        }

        var total = archives.Sum(file => file.Length);
        foreach (var file in archives.OrderBy(file => file.LastWriteTimeUtc))
        {
            if (total <= _capacityBytes)
                break;

            if (IsPinned(Path.GetFileNameWithoutExtension(file.Name)))
                continue;

            if (TryDelete(file.FullName))
            {
                total -= file.Length;
                deleted++;
            }
        }

        return deleted;
    }

    private int DeleteOtherVersions()
    {
        var parent = Path.GetDirectoryName(_cacheFolder);
        var own = Path.GetFileName(_cacheFolder);
        if (parent is null)
            return 0;

        var deleted = 0;
        try
        {
            var root = new DirectoryInfo(parent);
            if (!root.Exists)
                return 0;

            foreach (var folder in root.EnumerateDirectories("v*"))
            {
                // Only the folders of other cache versions, so that a folder a user put there stays.
                if (string.Equals(folder.Name, own, StringComparison.OrdinalIgnoreCase) || !IsVersionName(folder.Name) || folder.LinkTarget is not null)
                    continue;

                try
                {
                    folder.Delete(recursive: true);
                    deleted++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _log.Write($"Could not delete the old preview cache {folder.Name}.", exception);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.Write("Could not read the preview cache folder.", exception);
        }

        return deleted;
    }

    private void EnsureSpace(ModVersionMetadata release, IFreeSpaceProbe freeSpace)
    {
        var estimate = release.Download.SizeBytes ?? 0;
        var required = estimate + Math.Max(DriveInstallSpaceCheck.MinimumMarginBytes, (long)(estimate * DriveInstallSpaceCheck.MarginFraction));
        if (freeSpace.Measure(_archivesFolder) is { } volume && volume.AvailableBytes < required)
            throw new InsufficientDiskSpaceException(volume.VolumeName, required, volume.AvailableBytes);
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // On Windows a file that another reader holds open cannot be deleted, and the next sweep tries again.
            _log.Write($"Could not delete the preview cache file {Path.GetFileName(path)}.", exception);
            return false;
        }
    }

    private bool IsPinned(string key)
    {
        lock (_pins)
            return _pins.ContainsKey(key);
    }

    private void Unpin(string key)
    {
        lock (_pins)
        {
            var count = _pins.GetValueOrDefault(key);
            if (count <= 1)
                _pins.Remove(key);
            else
                _pins[key] = count - 1;
        }
    }

    private string ArchivePath(string key) => Path.Combine(_archivesFolder, key + ArchiveExtension);

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The hash as the file name holds it, so that no other text can become a path.</summary>
    private static string Key(string sha256)
    {
        if (!IsHash(sha256))
            throw new ArgumentException("The SHA-256 must be 64 hex characters.", nameof(sha256));

        return sha256.ToLowerInvariant();
    }

    /// <summary>True when the text is a SHA-256 in hex that can name a cached archive.</summary>
    internal static bool IsHash(string? sha256)
        => sha256 is not null && sha256.Length == HashLength && sha256.All(char.IsAsciiHexDigit);

    private static bool IsArchiveName(string name)
        => name.Length == HashLength + ArchiveExtension.Length
            && name.EndsWith(ArchiveExtension, StringComparison.Ordinal)
            && !name.AsSpan(0, HashLength).ContainsAnyExcept("0123456789abcdef");

    private static bool IsVersionName(string name)
        => name.Length > 1 && name[0] == 'v' && !name.AsSpan(1).ContainsAnyExceptInRange('0', '9');

    private sealed class PinHandle(PreviewArchiveCache cache, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                cache.Unpin(key);
        }
    }
}
