namespace Borea.Core.History;

public interface ITaskHistoryRepository
{
    /// <summary>The saved entries, newest first. A missing or unreadable history is empty.</summary>
    Task<IReadOnlyList<TaskHistoryEntry>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the saved history with the newest <see cref="TaskHistoryEntry.MaxEntries"/> entries, unless a newer Borea wrote it.</summary>
    Task SaveAsync(IReadOnlyList<TaskHistoryEntry> entries, CancellationToken cancellationToken = default);
}
