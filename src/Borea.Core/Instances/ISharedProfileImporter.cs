namespace Borea.Core.Instances;

/// <summary>
/// Creates an instance from copies of the mods in the shared profile in My
/// Games/Kitten Space Agency, and never moves, changes or deletes anything there.
/// </summary>
public interface ISharedProfileImporter
{
    /// <summary>
    /// The mod folders of the shared profile in load order, with the folders the
    /// manifest does not list last and disabled, the way the game adds them.
    /// </summary>
    Task<IReadOnlyList<SharedProfileMod>> GetModsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies every mod folder into a new instance with the same load order and
    /// enabled state, records a copy whose files match an index release as that
    /// release, and leaves no instance behind when it throws.
    /// </summary>
    Task<SharedProfileImportResult> ImportAsync(string instanceName, CancellationToken cancellationToken = default);
}
