namespace Borea.Core.Game;

/// <summary>
/// The master server's answer: the current public build and its download page.
/// Version is null when the answer did not parse; RawVersion keeps the original string.
/// </summary>
public sealed record LatestVersionInfo(GameVersion? Version, string RawVersion, string DownloadUrl);
