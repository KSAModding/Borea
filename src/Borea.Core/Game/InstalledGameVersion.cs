namespace Borea.Core.Game;

/// <summary>
/// The game build found on disk. Version is null when the string did not parse,
/// RawVersion keeps it either way.
/// </summary>
public sealed record InstalledGameVersion(GameVersion? Version, string RawVersion);
