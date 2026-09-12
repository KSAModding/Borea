using Borea.Core.Index;
using Borea.Core.Paths;

namespace Borea.Network.Index;

/// <summary>Refreshes and caches the one snapshot shared by all index repositories.</summary>
public sealed class ContentIndexSnapshotProvider : IContentIndexSnapshotProvider
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

    public ContentIndexSnapshotProvider(IContentIndexFetcher fetcher, IContentIndexReader reader, IGamePathProvider paths, TimeProvider? timeProvider = null)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_snapshot is not null && _timeProvider.GetUtcNow() - _lastAttemptAt < RevalidationInterval)
                return Task.FromResult(_snapshot);

            if (_inFlight is null || _inFlight.IsCompleted)
                _inFlight = RefreshAsync();

            return _inFlight.WaitAsync(cancellationToken);
        }
    }

    private async Task<ContentIndexSnapshot> RefreshAsync()
    {
        ContentIndexSnapshot? current;
        lock (_gate)
            current = _snapshot;

        try
        {
            var fetchResult = await _fetcher.FetchAsync(_paths.GetIndexPath()).ConfigureAwait(false);
            if (fetchResult == ContentIndexFetchResult.NotModified && current is not null)
                return RecordAttempt(current);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
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
