using Borea.Core.Mods;

namespace Borea.Core.Instances;

/// <summary>
/// The vehicles and saves Borea installed in an instance. A record is found by its type and its
/// folder name, ignoring case, because the game shows two folders whose names differ only in case
/// as one item.
/// </summary>
public interface IInstalledContentRecords
{
    Task<IReadOnlyList<InstalledContentRecord>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task<InstalledContentRecord?> FindAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default);

    /// <exception cref="InvalidOperationException">A record of that type and folder name exists.</exception>
    Task AddAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default);

    /// <exception cref="InvalidOperationException">No record of that type and folder name exists.</exception>
    Task ReplaceAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default);

    /// <returns>False when there was no such record.</returns>
    Task<bool> RemoveAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default);

    /// <summary>Moves the record to the new folder name and keeps its hashes.</summary>
    /// <returns>False when the old folder has no record.</returns>
    /// <exception cref="InvalidOperationException">Another record of that type has the new folder name.</exception>
    Task<bool> RenameFolderAsync(Guid instanceId, ContentType type, string oldFolderName, string newFolderName, CancellationToken cancellationToken = default);

    /// <summary>Compares the folder on disk with the hashes of the record. A missing folder has every file removed.</summary>
    Task<InstalledContentChanges> CompareAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default);

    /// <summary>Removes each record whose folder is gone, the way missing mods are found when an instance opens.</summary>
    /// <returns>The records it removed.</returns>
    Task<IReadOnlyList<InstalledContentRecord>> DropMissingAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
