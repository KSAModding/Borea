using Borea.Core.Game;
using Borea.Core.Paths;

namespace Borea.Storage.Game;

/// <summary>
/// Reads Content/Versions/*.json of the game folder in the format of <see cref="GamePatchNotesFile"/>. A file that does not parse is skipped.
/// </summary>
public sealed class FileGamePatchNotesReader : IGamePatchNotesReader
{
    private static readonly EnumerationOptions AllJsonFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly IGamePathProvider _pathProvider;

    public FileGamePatchNotesReader(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task<IReadOnlyList<GamePatchNotes>> ReadAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Read(_pathProvider.GetGameDirectoryPath(), cancellationToken), cancellationToken);

    private static IReadOnlyList<GamePatchNotes> Read(string? gameDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return [];

        var folder = Path.Combine(gameDirectory, "Content", "Versions");
        if (!Directory.Exists(folder))
            return [];

        var notes = new List<GamePatchNotes>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json", AllJsonFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReadFile(file) is { } entry)
                    notes.Add(entry);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NewestFirst(notes);
        }

        return NewestFirst(notes);
    }

    private static List<GamePatchNotes> NewestFirst(List<GamePatchNotes> notes) => notes
        .OrderByDescending(entry => entry.Revision)
        .ThenByDescending(entry => entry.Build, StringComparer.Ordinal)
        .ToList();

    private static GamePatchNotes? ReadFile(string path)
    {
        try
        {
            return GamePatchNotesFile.Parse(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
