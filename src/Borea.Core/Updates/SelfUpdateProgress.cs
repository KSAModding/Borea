namespace Borea.Core.Updates;

/// <summary>What a self-update is doing, and how much of the archive has arrived.</summary>
public readonly record struct SelfUpdateProgress(SelfUpdatePhase Phase, long BytesDownloaded = 0, long TotalBytes = 0)
{
    public double PercentComplete => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
}

/// <summary>The steps of a self-update, in the order they run.</summary>
public enum SelfUpdatePhase
{
    Downloading = 0,
    Verifying = 1,
    Unpacking = 2,
}
