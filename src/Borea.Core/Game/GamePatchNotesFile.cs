using System.Globalization;
using System.Text.Json;

namespace Borea.Core.Game;

/// <summary>
/// A Content/Versions file in the format of the game's ChangeLog class, which VersionHistory.Initialize loads.
/// </summary>
public static class GamePatchNotesFile
{
    /// <summary>The largest file Borea downloads or keeps.</summary>
    public const int MaxDownloadBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The game's file name for <paramref name="build"/>, such as "v2026.9.X.5438.json".</summary>
    public static string NameOf(GameVersion build) => NameOf(build.Year, build.Month, build.Revision);

    public static string NameOf(int year, int month, int revision)
        => string.Create(CultureInfo.InvariantCulture, $"v{year}.{month}.X.{revision}.json");

    /// <summary>The notes in <paramref name="utf8Json"/>, or null when it does not parse or has no build or commits.</summary>
    public static GamePatchNotes? Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.StartsWith("\uFEFF"u8))
            utf8Json = utf8Json[3..];

        ChangeLogDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ChangeLogDto>(utf8Json, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
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

        return new GamePatchNotes(dto.Build.Trim(), dto.FromRevision, dto.ToRevision, ParseDate(dto.Date), lines);
    }

    private static DateOnly? ParseDate(string? date)
        => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private sealed class ChangeLogDto
    {
        public string? Build { get; set; }

        public string? Date { get; set; }

        public int FromRevision { get; set; }

        public int ToRevision { get; set; }

        public List<CommitEntryDto?>? Commits { get; set; }
    }

    private sealed class CommitEntryDto
    {
        public int Rev { get; set; }

        public List<string?>? Lines { get; set; }
    }
}
