using System.Security.Cryptography;
using Borea.Core.Index;
using Borea.Core.Paths;
using Borea.Storage.Files;

namespace Borea.Storage.Images;

/// <summary>
/// Keeps image bytes in Borea's image cache folder, one file per SHA-256, and removes the
/// least recently used files when the folder grows past its size bound.
/// </summary>
public sealed class FileContentImageCache : IContentImageCache
{
    public const long DefaultMaxBytes = 128L * 1024 * 1024;

    private static readonly long MaxFileBytes = Math.Max(IconImage.MaxBytes, DescriptionImage.MaxBytes);

    private readonly IGamePathProvider _paths;
    private readonly long _maxBytes;

    public FileContentImageCache(IGamePathProvider paths, long maxBytes = DefaultMaxBytes)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

        if (maxBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "The cache size bound must be positive.");

        _maxBytes = maxBytes;
    }

    public async Task<byte[]?> ReadAsync(string sha256, CancellationToken cancellationToken = default)
    {
        var path = PathOf(sha256);
        byte[] bytes;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
                return null;

            if (file.Length > MaxFileBytes)
            {
                TryDelete(file);
                return null;
            }

            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(new FileInfo(path));
            return null;
        }

        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return bytes;
    }

    public async Task WriteAsync(string sha256, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var path = PathOf(sha256);
        await AtomicFile.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
        Evict(Path.GetFileName(path));
    }

    /// <summary>The last write time of a file is its last use, because a read sets it.</summary>
    private void Evict(string keptName)
    {
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(_paths.GetImageCacheFolder()).GetFiles();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var total = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
        {
            if (total <= _maxBytes)
                return;

            if (string.Equals(file.Name, keptName, StringComparison.Ordinal))
                continue;

            var length = file.Length;
            if (TryDelete(file))
                total -= length;
        }
    }

    private string PathOf(string sha256)
    {
        if (sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit))
            throw new ArgumentException("The digest must be 64 hex characters.", nameof(sha256));

        return Path.Combine(_paths.GetImageCacheFolder(), sha256.ToLowerInvariant());
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
