namespace Borea.Core.Game;

/// <summary>
/// Folders where the game or a mod loader may be installed. Nothing here is
/// checked yet.
/// </summary>
public interface IInstallCandidateSource
{
    IReadOnlyList<string> GetGameDirectories();

    IReadOnlyList<string> GetLoaderDirectories();
}
