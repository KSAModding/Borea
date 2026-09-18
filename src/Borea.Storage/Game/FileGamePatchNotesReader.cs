using System.Globalization;
using System.Text.Json;
using Borea.Core.Game;
using Borea.Core.Paths;

namespace Borea.Storage.Game;

/// <summary>
/// Reads Content/Versions/*.json of the game folder in the format of the game's ChangeLog class,
/// which VersionHistory.Initialize loads. A file that does not parse is skipped.
/// </summary>
public sealed class FileGamePatchNotesReader : IGamePatchNotesReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        ChangeLogDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ChangeLogDto>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Build) || dto.Commits is null)
            return null;

        var lines = dto.Commits
            .OfType<CommitEntryDto>()
            .OrderByDescending(commit => commit.Rev)
            .SelectMany(commit => commit.Lines ?? [])
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line!.Trim())
            .ToList();

        return new GamePatchNotes(dto.Build.Trim(), dto.ToRevision, ParseDate(dto.Date), lines);
    }

    private static DateOnly? ParseDate(string? date)
        => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private sealed class ChangeLogDto
    {
        public string? Build { get; set; }

        public string? Date { get; set; }

        public int ToRevision { get; set; }

        public List<CommitEntryDto?>? Commits { get; set; }
    }

    private sealed class CommitEntryDto
    {
        public int Rev { get; set; }

        public List<string?>? Lines { get; set; }
    }
}
