using Borea.Core.Mods;

namespace Borea.Core.Instances;

/// <summary>The created instance, every copied mod folder in load order, and whether the instance became the active one.</summary>
public sealed record SharedProfileImportResult(Instance Instance, IReadOnlyList<SharedProfileImportedMod> Mods, bool Activated);

/// <param name="HasManifestEntry">
/// False when the folder name is not a valid content id, so Borea wrote no
/// entry and the game adds the mod disabled on its next start.
/// </param>
/// <param name="Release">The index release the copy matched, or null for a manual install.</param>
/// <param name="MatchError">Why the copy could not be checked against the index, or null.</param>
public sealed record SharedProfileImportedMod(
    string FolderName,
    bool Enabled,
    bool HasManifestEntry,
    InstalledMod? Release,
    string? MatchError);
