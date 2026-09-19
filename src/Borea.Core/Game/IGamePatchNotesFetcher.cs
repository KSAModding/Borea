namespace Borea.Core.Game;

/// <summary>
/// Loads the patch notes of game builds that are not installed.
/// </summary>
public interface IGamePatchNotesFetcher
{
    /// <summary>
    /// The notes of the <paramref name="builds"/> above <paramref name="installedRevision"/>, and of the builds between them that the fromRevision of a loaded file names.
    /// A build whose changes the file of a newer build already lists has no file of its own and is skipped.
    /// At most <paramref name="maxFiles"/> files are read, newest first.
    /// </summary>
    Task<GamePatchNotesFetch> FetchAsync(IReadOnlyList<GameVersion> builds, int installedRevision, int maxFiles, CancellationToken cancellationToken = default);
}

/// <param name="Notes">The notes that loaded, newest first. A file that does not parse is left out.</param>
/// <param name="Complete">False when the file of a build could not be loaded.</param>
/// <param name="Capped">True when more builds were left than the files it could read.</param>
public sealed record GamePatchNotesFetch(IReadOnlyList<GamePatchNotes> Notes, bool Complete, bool Capped);
