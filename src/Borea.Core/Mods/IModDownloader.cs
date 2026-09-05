namespace Borea.Core.Mods;

public interface IModDownloader
{
    Task<DownloadResult> DownloadAsync(
        string modId,
        ModVersion version,
        string destinationDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public readonly record struct DownloadProgress(long BytesDownloaded, long TotalBytes)
{
    public double PercentComplete => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
}

/// <summary>
/// Outcome of a completed download, used by the caller to construct an
/// InstalledMod once metadata and install reason are also known.
/// </summary>
public readonly record struct DownloadResult(string ModId, long BytesDownloaded, string? Checksum);
