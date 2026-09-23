namespace Borea.Core.Instances;

/// <summary>
/// The backups that Back up, Delete, a replacing copy and a replacing restore
/// leave in the Backups folder. A change that fails leaves every save and vehicle as it was.
/// </summary>
public interface IGameSaveBackupStore
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<GameSaveBackup>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the backup back where it came from. A folder of the same name is replaced only when
    /// <paramref name="replace"/> is true, after it moves into the backups. A zip stays in the backups.
    /// </summary>
    Task<GameSaveRestoreOutcome> RestoreAsync(GameSaveBackup backup, bool replace, CancellationToken cancellationToken = default);

    Task DeleteAsync(GameSaveBackup backup, CancellationToken cancellationToken = default);

    /// <summary>Deletes the backups of every instance made before <paramref name="cutoff"/> and returns how many.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

public enum GameSaveRestoreOutcome
{
    Restored,

    /// <summary>The instance holds a folder of that name, so nothing was restored.</summary>
    Exists,
}
