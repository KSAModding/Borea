using System.Text.Json;
using Borea.Core.History;
using Borea.Core.Paths;
using Borea.Storage.Files;

namespace Borea.Storage.History;

public sealed class FileTaskHistoryRepository : ITaskHistoryRepository
{
    internal const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IGamePathProvider _pathProvider;

    public FileTaskHistoryRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<IReadOnlyList<TaskHistoryEntry>> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var text = await File.ReadAllTextAsync(_pathProvider.GetTaskHistoryPath(), cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<TaskHistoryDocumentDto>(text, JsonOptions);
            if (document?.FormatVersion != CurrentFormatVersion || document.Tasks is null)
                return [];

            return document.Tasks
                .Select(FromDto)
                .OfType<TaskHistoryEntry>()
                .OrderByDescending(entry => entry.EndedAt)
                .Take(TaskHistoryEntry.MaxEntries)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<TaskHistoryEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var path = _pathProvider.GetTaskHistoryPath();
        if (await FormatVersionAsync(path, cancellationToken).ConfigureAwait(false) > CurrentFormatVersion)
            return;

        var document = new TaskHistoryDocumentDto
        {
            FormatVersion = CurrentFormatVersion,
            Tasks = entries
                .Where(entry => HasEnded(entry.State))
                .OrderByDescending(entry => entry.EndedAt)
                .Take(TaskHistoryEntry.MaxEntries)
                .Select(ToDto)
                .ToList<TaskHistoryEntryDto?>(),
        };

        await AtomicFile.WriteAllTextAsync(path, JsonSerializer.Serialize(document, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int?> FormatVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<TaskHistoryVersionDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return document?.FormatVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasEnded(TaskState state) => state is TaskState.Finished or TaskState.Stopped or TaskState.Failed;

    private static TaskHistoryEntryDto ToDto(TaskHistoryEntry entry) => new()
    {
        Kind = NameOf(entry.Kind),
        State = NameOf(entry.State),
        Subject = entry.Subject,
        InstanceId = entry.InstanceId,
        InstanceName = entry.InstanceName,
        StartedAt = entry.StartedAt,
        EndedAt = entry.EndedAt,
        FailureReason = entry.FailureReason,
        ContentId = entry.ContentId,
        Version = entry.Version,
    };

    /// <summary>Null for an entry that a newer Borea wrote with a kind or state this one does not know.</summary>
    private static TaskHistoryEntry? FromDto(TaskHistoryEntryDto? dto)
    {
        if (dto is not { StartedAt: { } startedAt, EndedAt: { } endedAt }
            || !TryRead<TaskKind>(dto.Kind, out var kind)
            || !TryRead<TaskState>(dto.State, out var state)
            || !HasEnded(state))
            return null;

        return new TaskHistoryEntry(kind, state, dto.Subject, dto.InstanceId, dto.InstanceName, startedAt, endedAt, dto.FailureReason, dto.ContentId, dto.Version);
    }

    private static string NameOf<T>(T value)
        where T : struct, Enum
        => JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString());

    private static bool TryRead<T>(string? name, out T value)
        where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (NameOf(candidate) == name)
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
