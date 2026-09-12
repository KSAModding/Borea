using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Network.Index;

/// <summary>
/// Serves the community index through <see cref="IModRepository"/>.
/// Successful snapshots are revalidated at most once per index watcher interval,
/// and overlapping callers share one refresh and parse operation.
/// </summary>
public sealed class ContentIndexModRepository : IContentIndexRepository, IModIdClaimSource
{
    public const string SourceName = "index";

    private static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(10);

    private readonly IContentIndexFetcher _fetcher;
    private readonly IContentIndexReader _reader;
    private readonly IGamePathProvider _paths;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    private ContentIndexSnapshot? _snapshot;
    private DateTimeOffset _lastAttemptAt;
    private Task<ContentIndexSnapshot>? _inFlight;

    public ContentIndexModRepository(
        IContentIndexFetcher fetcher,
        IContentIndexReader reader,
        IGamePathProvider paths,
        TimeProvider? timeProvider = null)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return AvailableListings(snapshot).Select(listing => listing.Authored!).ToArray();
    }

    public async Task<ModVersionMetadata?> GetLatestReleaseAsync(
        string modId,
        CancellationToken cancellationToken = default)
    {
        var listing = await FindListingAsync(modId, cancellationToken).ConfigureAwait(false);
        return listing?.Releases
            .Where(release => !release.Yanked)
            .OrderByDescending(release => release.Version)
            .FirstOrDefault();
    }

    public async Task<ModVersionMetadata?> GetReleaseAsync(
        string modId,
        ModVersion version,
        CancellationToken cancellationToken = default)
    {
        var listing = await FindListingAsync(modId, cancellationToken).ConfigureAwait(false);
        return listing?.Releases.FirstOrDefault(release => release.Version.Equals(version));
    }

    public async Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(
        string modId,
        CancellationToken cancellationToken = default)
    {
        var listing = await FindListingAsync(modId, cancellationToken).ConfigureAwait(false);
        return listing?.Releases
            .Where(release => !release.Yanked)
            .Select(release => release.Version)
            .Distinct()
            .OrderByDescending(version => version)
            .ToArray()
            ?? Array.Empty<ModVersion>();
    }

    public async Task<IReadOnlyList<ModMetadata>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return AvailableListings(snapshot)
            .Select(listing => listing.Authored!)
            .Where(mod => Matches(mod, query))
            .ToArray();
    }

    public async Task<IReadOnlyList<ContentIndexDiagnostic>> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Diagnostics;
    }

    public async Task<IReadOnlyList<string>> GetClaimedModIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Listings.Select(listing => listing.Id)
            .Concat(snapshot.Packs.Select(pack => pack.Id))
            .Concat(snapshot.Diagnostics
                .Where(diagnostic => diagnostic.Scope is ContentIndexDiagnosticScope.Listing or ContentIndexDiagnosticScope.Pack)
                .Select(diagnostic => diagnostic.Id)
                .Where(ModIds.IsValid)
                .Select(id => id!))
            .Distinct(ModIds.Comparer)
            .ToArray();
    }

    private async Task<ContentIndexListing?> FindListingAsync(
        string modId,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return AvailableListings(snapshot).FirstOrDefault(listing => ModIds.Equals(listing.Id, modId));
    }

    private Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_snapshot is not null
                && _timeProvider.GetUtcNow() - _lastAttemptAt < RevalidationInterval)
            {
                return Task.FromResult(_snapshot);
            }

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
            var loaded = await _reader.ReadAsync().ConfigureAwait(false);
            return RecordAttempt(loaded);
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

    private static IEnumerable<ContentIndexListing> AvailableListings(ContentIndexSnapshot snapshot) =>
        snapshot.Listings.Where(listing =>
            listing.Authored is { Type: ContentType.Mod or ContentType.ModLoader }
            && listing.IndexStatus?.State != IndexStatusState.Delisted);

    private static bool Matches(ModMetadata mod, string query) =>
        Contains(mod.ModId, query)
        || Contains(mod.Name, query)
        || Contains(mod.Abstract, query)
        || Contains(mod.Description, query)
        || mod.Tags.Any(tag => Contains(tag, query));

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
