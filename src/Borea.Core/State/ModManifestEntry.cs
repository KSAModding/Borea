namespace Borea.Core.State;

/// <summary>
/// One manifest.toml entry. List position is load order (ModLibrary.PrepareAll).
/// <see cref="ModId"/> is empty when the entry names no mod.
/// </summary>
public sealed record ModManifestEntry(string ModId, bool Enabled);
