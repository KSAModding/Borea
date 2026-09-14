using Borea.Core.Index;
using Borea.Core.Paths;

namespace Borea.Network.Index;

/// <summary>Refreshes and caches the one snapshot shared by all index repositories.</summary>
public sealed class ContentIndexSnapshotProvider : IContentIndexSnapshotProvider, IContentIndexRefresh
{
    private static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(10);
    private readonly IContentIndexFetcher _fetcher;
    private readonly IContentIndexReader _reader;
    private readonly IGamePathProvider _paths;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private ContentIndexSnapshot? _snapshot;
    private DateTimeOffset _lastAttemptAt;
    private Task<ContentIndexSnapshot>? _inFlight;
    private ContentIndexRefreshOutcome _lastOutcome = ContentIndexRefreshOutcome.NotAttempted;
    private string? _failureReason;

    public ContentIndexSnapshotProvider(IContentIndexFetcher fetcher, IContentIndexReader reader, IGamePathProvider paths, TimeProvider? timeProvider = null)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The cache age is the last write time of the cached file, which a successful fetch sets.</summary>
    public ContentIndexRefreshStatus Status
    {
        get
        {
            ContentIndexRefreshOutcome outcome;
            string? reason;
            lock (_gate)
            {
                outcome = _lastOutcome;
                reason = _failureReason;
            }

            var indexPath = _paths.GetIndexPath();
            DateTimeOffset? cachedAt = File.Exists(indexPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(indexPath), TimeSpan.Zero)
                : null;
            return new ContentIndexRefreshStatus(outcome, cachedAt, reason);
        }
    }

    public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_snapshot is not null && _timeProvider.GetUtcNow() - _lastAttemptAt < RevalidationInterval)
                return Task.FromResult(_snapshot);

            return StartRefresh().WaitAsync(cancellationToken);
        }
    }

    public Task<ContentIndexSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return StartRefresh().WaitAsync(cancellationToken);
    }

    private Task<ContentIndexSnapshot> StartRefresh()
    {
        if (_inFlight is null || _inFlight.IsCompleted)
            _inFlight = RefreshCoreAsync();

        return _inFlight;
    }

    private async Task<ContentIndexSnapshot> RefreshCoreAsync()
    {
        ContentIndexSnapshot? current;
        lock (_gate)
            current = _snapshot;

        var indexPath = _paths.GetIndexPath();
        try
        {
            var fetchResult = await _fetcher.FetchAsync(indexPath).ConfigureAwait(false);
            RecordFetch(indexPath, fetchResult);
            if (fetchResult == ContentIndexFetchResult.NotModified && current is not null)
                return RecordAttempt(current);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            RecordOutcome(ContentIndexRefreshOutcome.Failed, ex.Message);
            if (current is not null)
                return RecordAttempt(current);
        }

        try
        {
            return RecordAttempt(await _reader.ReadAsync().ConfigureAwait(false));
        }
        catch (Exception ex) when (current is not null && ex is (InvalidOperationException or IOException))
        {
            return RecordAttempt(current);
        }
    }

    private void RecordFetch(string indexPath, ContentIndexFetchResult fetchResult)
    {
        // a 304 confirms the cached file, so its age starts again too
        try
        {
            if (File.Exists(indexPath))
                File.SetLastWriteTimeUtc(indexPath, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the file keeps its older time, which only makes the cache look older
        }

        RecordOutcome(
            fetchResult == ContentIndexFetchResult.Downloaded ? ContentIndexRefreshOutcome.Downloaded : ContentIndexRefreshOutcome.NotModified,
            failureReason: null);
    }

    private void RecordOutcome(ContentIndexRefreshOutcome outcome, string? failureReason)
    {
        lock (_gate)
        {
            _lastOutcome = outcome;
            _failureReason = failureReason;
        }
    }

    private ContentIndexSnapshot RecordAttempt(ContentIndexSnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
            _lastAttemptAt = _timeProvider.GetUtcNow();
            return snapshot;
        }
    }
}
