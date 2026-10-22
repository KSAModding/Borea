using Borea.Core.Instances;

namespace Borea.Core.Sharing;

/// <summary>
/// Reads the folder of a vehicle or a save the way a client that shares it does (RFC 0085): meta.toml, and the ids that the game file
/// names, without the game's types.
/// </summary>
public interface IContentIdReader
{
    /// <exception cref="GameFolderException">meta.toml or the game file is missing or cannot be read.</exception>
    /// <exception cref="IOException">A file could not be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">A file could not be opened.</exception>
    Task<GameFolderContent> ReadAsync(string folder, GameSaveKind kind, CancellationToken cancellationToken = default);
}

/// <param name="Name">The name in meta.toml, which is also the folder the game installs it as.</param>
/// <param name="GameBuild">The version in meta.toml, the build that wrote the file.</param>
/// <param name="Clutter">The ground clutter of a save, by celestial and ecotype.</param>
/// <param name="ExtraFiles">Every file other than meta.toml and the game file, relative to the folder with '/', sorted ordinal.</param>
public sealed record GameFolderContent(string Name, string? GameBuild, ContentIds Ids, IReadOnlyList<GroundClutterRef> Clutter, IReadOnlyList<string> ExtraFiles);

public sealed record GroundClutterRef(string Celestial, string Ecotype);

public enum GameFolderProblem
{
    MetadataMissing,
    MetadataUnreadable,
    NameMissing,
    GameFileMissing,
    GameFileNotWellFormed,
}

/// <summary>The folder is not one the game reads. The message names the file.</summary>
public sealed class GameFolderException : Exception
{
    public GameFolderException(GameFolderProblem problem, string path, string? detail = null, Exception? innerException = null)
        : base(MessageOf(problem, path, detail), innerException)
    {
        Problem = problem;
        Path = path;
        Detail = detail;
    }

    public GameFolderProblem Problem { get; }

    public string Path { get; }

    public string? Detail { get; }

    private static string MessageOf(GameFolderProblem problem, string path, string? detail)
    {
        var message = problem switch
        {
            GameFolderProblem.MetadataMissing or GameFolderProblem.GameFileMissing => $"{path} does not exist.",
            GameFolderProblem.MetadataUnreadable => $"{path} is not TOML.",
            GameFolderProblem.NameMissing => $"{path} has no name.",
            _ => $"{path} is not well-formed XML.",
        };
        return detail is null ? message : $"{message} {detail}";
    }
}
