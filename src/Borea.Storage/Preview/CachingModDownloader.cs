using System.Security.Cryptography;
using Borea.Core.Logging;
using Borea.Core.Mods;

namespace Borea.Storage.Preview;

/// <summary>
/// An <see cref="IModDownloader"/> that serves a release archive from the <see cref="PreviewArchiveCache"/>
/// when the vehicle preview already fetched it, so an install after a preview does not download the same
/// bytes again. An install that starts while the preview still fetches the same file waits for that fetch and
/// uses its result. Every other request goes to the inner downloader.
/// </summary>
/// <remarks>
/// Only a release with a SHA-256 can use the cache, because the hash is the name of the cached file and the
/// only proof that the file holds the release. The copy is hashed while it is written, and a cached file
/// with other bytes is deleted before the inner downloader runs, so a result from this class names verified
/// bytes, as the contract of <see cref="IModDownloader"/> asks. The constructor reads and writes nothing.
/// </remarks>
public sealed class CachingModDownloader : IModDownloader
{
    private const int BufferSize = 81920;

    private readonly IModDownloader _inner;
    private readonly PreviewArchiveCache _cache;
    private readonly IBoreaLog _log;

    public CachingModDownloader(IModDownloader inner, PreviewArchiveCache cache, IBoreaLog log)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The downloader that serves every request the cache cannot serve.</summary>
    internal IModDownloader Inner => _inner;

    public async Task<DownloadResult> DownloadAsync(
        ModVersionMetadata release,
        string archivePath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("Archive path cannot be null or whitespace.", nameof(archivePath));

        var sha256 = release.Download.Sha256;
        if (PreviewArchiveCache.IsHash(sha256) && await _cache.TryGetAfterFetchAsync(sha256!, cancellationToken).ConfigureAwait(false) is { } cached
            && await TryCopyAsync(release, cached, archivePath, sha256!, progress, cancellationToken).ConfigureAwait(false) is { } result)
        {
            return result;
        }

        return await _inner.DownloadAsync(release, archivePath, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies the cached file to the target and returns the result, or null when the file is gone or its bytes
    /// have another hash. In both cases nothing stays at the target, so the inner downloader starts clean.
    /// </summary>
    private async Task<DownloadResult?> TryCopyAsync(
        ModVersionMetadata release,
        string cachedPath,
        string archivePath,
        string sha256,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string actual;
        long copied = 0;
        try
        {
            await using (var source = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
            await using (var target = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var total = source.Length;
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copied += read;
                    progress?.Report(new DownloadProgress(copied, total));
                }

                actual = Convert.ToHexString(hash.GetHashAndReset());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A sweep or another process can delete the cached file at any time, and then the network has to serve it.
            _log.Write($"Could not copy the cached archive of {release.ModId} {release.Version}, so it is downloaded.", exception);
            TryDeleteTarget(archivePath);
            return null;
        }
        catch
        {
            TryDeleteTarget(archivePath);
            throw;
        }

        if (string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            return new DownloadResult(release.Download.Url, copied, actual);

        _log.Write($"The cached archive of {release.ModId} {release.Version} has the SHA-256 {actual}, and the release names {sha256}. The file is deleted and the archive is downloaded.");
        TryDeleteTarget(archivePath);
        _cache.Remove(sha256);
        return null;
    }

    private void TryDeleteTarget(string archivePath)
    {
        try
        {
            File.Delete(archivePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The inner downloader writes over the same path, so a file left here does no harm to a later download.
            _log.Write($"Could not delete the partial copy {Path.GetFileName(archivePath)}.", exception);
        }
    }
}
