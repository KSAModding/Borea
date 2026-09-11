namespace Borea.Core.Mods;

/// <summary>
/// Fetches the archive of one release. Host-agnostic: the release file carries
/// an absolute URL, its mirrors and the hash, so GitHub, SpaceDock and any later
/// host go through the same path and none needs code of its own.
/// </summary>
public interface IModDownloader
{
    /// <summary>
    /// Writes the release archive to <paramref name="archivePath"/>, from
    /// <see cref="DownloadInfo.Url"/> first and each of <see cref="DownloadInfo.Mirrors"/>
    /// after it, and verifies the bytes against <see cref="DownloadInfo.Sha256"/>
    /// before returning. A mismatch counts as a failed source, never as a warning,
    /// so a returned result always names bytes that passed. A release without a
    /// hash is accepted as received; the caller knows from the null hash that
    /// nothing verified it. Nothing is left at <paramref name="archivePath"/>
    /// when every source failed.
    /// </summary>
    /// <exception cref="DownloadFailedException">Every source failed or served other bytes.</exception>
    Task<DownloadResult> DownloadAsync(
        ModVersionMetadata release,
        string archivePath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public readonly record struct DownloadProgress(long BytesDownloaded, long TotalBytes)
{
    public double PercentComplete => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
}

/// <summary>
/// Outcome of a completed download.
/// </summary>
/// <param name="Url">The source that served the archive.</param>
/// <param name="BytesDownloaded">The archive size as received.</param>
/// <param name="Sha256">Uppercase hex SHA-256 of the received bytes.</param>
public readonly record struct DownloadResult(string Url, long BytesDownloaded, string Sha256);
