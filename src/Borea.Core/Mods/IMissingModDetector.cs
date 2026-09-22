namespace Borea.Core.Mods;

/// <summary>
/// The other direction of <see cref="IForeignModAdopter"/>: a mod the instance
/// records while the folder the game would load it from is gone. The record
/// alone decides nothing, so Borea compares it with the folders when an
/// instance opens.
/// </summary>
public interface IMissingModDetector
{
    /// <summary>
    /// The ids of the recorded mods that have no folder with a mod.toml, in the
    /// order the instance records them.
    /// </summary>
    Task<IReadOnlyList<string>> ScanAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the record of a mod whose folder is gone, returning whether it
    /// did. A mod whose folder is there stays, whoever owns its files. The
    /// manifest entry stays, because the game drops an entry that names no
    /// folder on its next launch.
    /// </summary>
    Task<bool> DropAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The name of the folder that carries the id of a recorded mod but no
    /// mod.toml, or null when the mod has no folder at all. The game ignores
    /// such a folder and an install cannot write over it, so it is what a
    /// message names when Borea cannot install the mod again.
    /// </summary>
    Task<string?> FindLeftoverFolderAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);
}
