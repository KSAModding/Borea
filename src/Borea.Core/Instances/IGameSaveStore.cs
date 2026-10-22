namespace Borea.Core.Instances;

/// <summary>
/// A change that fails leaves every save and vehicle as it was.
/// </summary>
public interface IGameSaveStore
{
    /// <summary>In the letter case found on disk.</summary>
    string GetFolder(Guid instanceId, GameSaveKind kind);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<GameSaveEntry>> ListAsync(Guid instanceId, GameSaveKind kind, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<GameSaveEntry>> ListSharedProfileAsync(GameSaveKind kind, CancellationToken cancellationToken = default);

    /// <summary>True when the game profile holds a folder of this kind. Reads no metadata.</summary>
    Task<bool> HasSharedProfileItemsAsync(GameSaveKind kind, CancellationToken cancellationToken = default);

    /// <summary>Returns the path of the zip.</summary>
    Task<string> BackUpAsync(Guid instanceId, GameSaveEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Keeps no zip when one folder fails.</summary>
    Task<IReadOnlyList<string>> BackUpAllAsync(Guid instanceId, GameSaveKind kind, CancellationToken cancellationToken = default);

    /// <summary>Replaces a folder of the same name only when <paramref name="replace"/> is true, after it moves the old folder into the backups.</summary>
    Task<GameSaveCopyOutcome> CopyAsync(GameSaveEntry entry, Guid targetInstanceId, bool replace, CancellationToken cancellationToken = default);

    /// <summary>Moves the folder into the backups and returns its new path.</summary>
    Task<string> DeleteAsync(Guid instanceId, GameSaveEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="newName"/> into meta.toml and renames the folder to match.
    /// The game lists the item by the name in meta.toml and writes its next save of the
    /// item into a folder of that name, so a folder of another name stays behind as a
    /// second copy.
    /// </summary>
    Task<GameSaveRenameOutcome> RenameAsync(Guid instanceId, GameSaveEntry entry, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the zip a vehicle or a save is shared as (RFC 0085): the folder as it is, under one top-level directory named
    /// <paramref name="name"/>. Changes nothing in the folder.
    /// </summary>
    /// <param name="name">The name in meta.toml that the share was checked with.</param>
    /// <exception cref="InvalidOperationException">meta.toml no longer holds <paramref name="name"/>.</exception>
    Task<GameSaveArchive> WriteShareArchiveAsync(Guid instanceId, GameSaveEntry entry, string name, Stream destination, CancellationToken cancellationToken = default);
}

/// <param name="Sha256">The SHA-256 of the zip, in lower-case hex.</param>
/// <param name="Size">The size of the zip in bytes.</param>
public sealed record GameSaveArchive(string Sha256, long Size);

public enum GameSaveCopyOutcome
{
    Copied,

    /// <summary>The instance holds a folder of that name, so nothing was copied.</summary>
    Exists,
}

public enum GameSaveRenameOutcome
{
    Renamed,

    /// <summary>The game would change the name, see <see cref="GameSaveName"/>. Nothing was changed.</summary>
    InvalidName,

    /// <summary>
    /// Another item of the same kind in the instance has the name or a folder of that
    /// name, in any letter case, so the game would show only one of the two. Nothing was changed.
    /// </summary>
    NameTaken,
}
