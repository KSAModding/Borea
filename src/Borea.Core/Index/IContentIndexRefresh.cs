namespace Borea.Core.Index;

/// <summary>Refreshes the shared content index snapshot on request and reports how the last refresh went.</summary>
public interface IContentIndexRefresh
{
    ContentIndexRefreshStatus Status { get; }

    /// <summary>Fetches and reads the index even when the shared snapshot is still fresh.</summary>
    Task<ContentIndexSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}
