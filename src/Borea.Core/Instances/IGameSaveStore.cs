namespace Borea.Core.Instances;

/// <summary>
/// A change throws <see cref="GameSaveInUseException"/> before it touches a folder that holds a file in use.
/// </summary>
public interface IGameSaveStore
{
    /// <summary>In the letter case found on disk.</summary>
    string GetFolder(Guid instanceId, GameSaveKind kind);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<GameSaveEntry>> ListAsync(Guid instanceId, GameSaveKind kind, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<GameSaveEntry>> ListSharedProfileAsync(GameSaveKind kind, CancellationToken cancellationToken = default);

    /// <summary>Returns the path of the zip.</summary>
    Task<string> BackUpAsync(Guid instanceId, GameSaveEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Writes no zip when a file in any folder is in use.</summary>
    Task<IReadOnlyList<string>> BackUpAllAsync(Guid instanceId, GameSaveKind kind, CancellationToken cancellationToken = default);

    /// <summary>Replaces a folder of the same name only when <paramref name="replace"/> is true, after it moves the old folder into the backups.</summary>
    Task<GameSaveCopyOutcome> CopyAsync(GameSaveEntry entry, Guid targetInstanceId, bool replace, CancellationToken cancellationToken = default);

    /// <summary>Moves the folder into the backups and returns its new path.</summary>
    Task<string> DeleteAsync(Guid instanceId, GameSaveEntry entry, CancellationToken cancellationToken = default);
}

public enum GameSaveCopyOutcome
{
    Copied,

    /// <summary>The instance holds a folder of that name, so nothing was copied.</summary>
    Exists,
}
