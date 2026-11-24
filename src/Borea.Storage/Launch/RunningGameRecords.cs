using System.Text.Json;
using Borea.Storage.Files;

namespace Borea.Storage.Launch;

/// <summary>
/// The file of the game processes that Borea started for an instance, with the id and the start time of each process.
/// Borea can close while the game runs, and a Borea that opens later reads this file to know which instances still run,
/// so it does not start one of them a second time. A record whose process has ended is dropped at the next write.
/// </summary>
internal static class RunningGameRecords
{
    private const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>The records in the file. An unreadable file or a newer format gives none, as no record does.</summary>
    public static IReadOnlyList<RunningGameRecord> Read(string path)
    {
        try
        {
            var document = JsonSerializer.Deserialize<DocumentDto>(File.ReadAllText(path), JsonOptions);
            if (document?.FormatVersion != CurrentFormatVersion || document.Games is null)
                return [];

            return document.Games
                .Where(game => game is { Instance: not null, ProcessId: > 0, StartedAt: not null })
                .Select(game => new RunningGameRecord(game!.Instance!.Value, game.ProcessId, game.StartedAt!.Value))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes the records that still run, with <paramref name="record"/> in place of an earlier one of its instance.
    /// </summary>
    /// <param name="stillRuns">Whether the process of a record runs.</param>
    public static void Put(string path, RunningGameRecord record, Func<RunningGameRecord, bool> stillRuns)
    {
        var games = Read(path)
            .Where(game => game.InstanceId != record.InstanceId && stillRuns(game))
            .Append(record)
            .Select(game => new GameDto { Instance = game.InstanceId, ProcessId = game.ProcessId, StartedAt = game.StartedAt })
            .ToList<GameDto?>();

        var document = new DocumentDto { FormatVersion = CurrentFormatVersion, Games = games };
        AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
    }

    private sealed class DocumentDto
    {
        public int? FormatVersion { get; set; }

        public List<GameDto?>? Games { get; set; }
    }

    private sealed class GameDto
    {
        public Guid? Instance { get; set; }

        public int ProcessId { get; set; }

        public DateTime? StartedAt { get; set; }
    }
}

/// <summary>A game process that Borea started for an instance.</summary>
internal sealed record RunningGameRecord(Guid InstanceId, int ProcessId, DateTime StartedAt);
