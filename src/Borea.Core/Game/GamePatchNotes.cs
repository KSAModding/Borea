namespace Borea.Core.Game;

/// <summary>
/// The patch notes of one game build, as the game ships them in Content/Versions.
/// </summary>
/// <param name="Build">The build as the file names it, such as "2026.9.10.5438".</param>
/// <param name="FromRevision">The revision before the first change, or 0 when the file gives none.</param>
/// <param name="Revision">The revision the notes lead up to.</param>
/// <param name="Date">The day of the build, or null when the file gives none.</param>
/// <param name="Lines">The changes, newest commit first.</param>
public sealed record GamePatchNotes(string Build, int FromRevision, int Revision, DateOnly? Date, IReadOnlyList<string> Lines);
