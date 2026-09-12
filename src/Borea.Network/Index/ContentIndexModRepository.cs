using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Network.Index;

/// <summary>
/// Serves the community index through <see cref="IModRepository"/>.
/// The repository uses the snapshot provider shared with other index content.
/// </summary>
public sealed class ContentIndexModRepository : IContentIndexRepository, IModIdClaimSource, IModArchiveReleaseLookup
{
    public const string SourceName = "index";

    private readonly IContentIndexSnapshotProvider _snapshots;

    public ContentIndexModRepository(
        IContentIndexFetcher fetcher,
        IContentIndexReader reader,
        IGamePathProvider paths,
        TimeProvider? timeProvider = null)
        : this(new ContentIndexSnapshotProvider(fetcher, reader, paths, timeProvider))
    {
    }

    public ContentIndexModRepository(IContentIndexSnapshotProvider snapshots)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
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

    public async Task<ModVersionMetadata?> FindBySha256Async(
        string modId,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        var listing = await FindListingAsync(modId, cancellationToken).ConfigureAwait(false);
        if (listing?.Authored?.Type != ContentType.Mod)
            return null;

        var matches = listing.Releases
            .Where(release => release.Type == ContentType.Mod)
            .Where(release => ModIds.Equals(release.ModId, listing.Id))
            .Where(release => string.Equals(release.Download.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
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
        => _snapshots.GetSnapshotAsync(cancellationToken);

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
