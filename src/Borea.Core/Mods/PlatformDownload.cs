namespace Borea.Core.Mods;

/// <summary>
/// The archive of a release for one platform, which a client on that platform takes in place of the archive for every platform.
/// </summary>
/// <param name="InstallSizeBytes">The unpacked size of this archive, which can differ from the one of the archive for every platform.</param>
public sealed record PlatformDownload(DownloadInfo Download, long InstallSizeBytes);
