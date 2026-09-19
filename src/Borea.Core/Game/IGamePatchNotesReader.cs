namespace Borea.Core.Game;

/// <summary>
/// Reads the patch notes the installed game ships, without a request.
/// </summary>
public interface IGamePatchNotesReader
{
    /// <summary>
    /// The notes of every build in the game folder, newest first. Empty when no game folder is set.
    /// </summary>
    Task<IReadOnlyList<GamePatchNotes>> ReadAsync(CancellationToken cancellationToken = default);
}
