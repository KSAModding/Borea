namespace Borea.Core.State;

/// <summary>
/// Reads and writes the manifest that decides which mods an instance loads.
/// The file belongs to the game, so this holds no state of its own in it and re-reads
/// it before every write. Callers running operations in parallel serialize them
/// per instance themselves and nothing here does. Re-reading also does not close
/// the window against the running game, which rewrites the file from a snapshot
/// of its own (GameSettings.SaveChanges).
/// </summary>
public interface IModStateRepository
{
    /// <summary>Every entry, in load order. Empty when there is no manifest.</summary>
    Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Whether any entry naming the mod is enabled.</summary>
    Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);

    /// <summary>One id per active mod, in load order.</summary>
    Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an entry for a mod that is on disk, at the end. Writes
    /// nothing while the files are missing, and nothing over an entry that
    /// exists, so a mod disabled in the game stays disabled through an install.
    /// </summary>
    Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the game load the mod, returning whether that changed anything.
    /// Creates nothing; that is <see cref="AddEntryAsync"/>.
    /// </summary>
    Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);

    /// <summary>Stops the game loading the mod, returning whether that changed anything.</summary>
    Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the entries in the given order, returning whether the order changed.
    /// The only operation that moves an entry.
    /// </summary>
    /// <param name="modIds">Every entry naming a mod, once each, in the wanted order.</param>
    /// <exception cref="ArgumentException">
    /// The list does not name exactly those entries. Also fires when the file
    /// changed since <see cref="GetEntriesAsync"/>; read it again.
    /// </exception>
    Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default);
}
