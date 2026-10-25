using Borea.Core.Mods;

namespace Borea.Network.Sources;

/// <summary>
/// Queries every registered source and merges results, tagging each listing
/// and release with its originating source via the Source property. The first
/// registered source wins when sources share an id.
/// </summary>
public sealed class CompositeModRepository : IModRepository
{
    private readonly IReadOnlyDictionary<string, IModRepository> _sources;

    public CompositeModRepository(IReadOnlyDictionary<string, IModRepository> sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
        MergeListingsAsync(repository => repository.GetAvailableModsAsync(cancellationToken), cancellationToken);

    public async Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default)
    {
        foreach (var (source, repository) in _sources)
        {
            var listing = await repository.GetListingAsync(modId, cancellationToken).ConfigureAwait(false);
            if (listing is not null)
                return Tag(listing, source);

            if (await ClaimsAsync(repository, modId, cancellationToken).ConfigureAwait(false))
                return null;
        }

        return null;
    }

    public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
        FirstReleaseAsync(modId, repository => repository.GetLatestReleaseAsync(modId, cancellationToken), cancellationToken);

    public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
        FirstReleaseAsync(modId, repository => repository.GetReleaseAsync(modId, version, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
    {
        foreach (var repository in _sources.Values)
        {
            var versions = await repository.GetAvailableVersionsAsync(modId, cancellationToken).ConfigureAwait(false);
            if (versions.Count > 0)
                return versions;

            if (await ClaimsAsync(repository, modId, cancellationToken).ConfigureAwait(false))
                return Array.Empty<ModVersion>();
        }
        return Array.Empty<ModVersion>();
    }

    public async Task<IReadOnlyList<ModVersionMetadata>> GetReleaseHistoryAsync(string modId, CancellationToken cancellationToken = default)
    {
        foreach (var (source, repository) in _sources)
        {
            var releases = await repository.GetReleaseHistoryAsync(modId, cancellationToken).ConfigureAwait(false);
            if (releases.Count > 0)
                return releases.Select(release => Tag(release, source)).ToArray();

            if (await ClaimsAsync(repository, modId, cancellationToken).ConfigureAwait(false))
                return Array.Empty<ModVersionMetadata>();
        }
        return Array.Empty<ModVersionMetadata>();
    }

    public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        MergeListingsAsync(repository => repository.SearchAsync(query, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<ModMetadata>> MergeListingsAsync(
        Func<IModRepository, Task<IReadOnlyList<ModMetadata>>> listMods,
        CancellationToken cancellationToken)
    {
        var results = new List<ModMetadata>();
        var seenIds = new HashSet<string>(ModIds.Comparer);
        foreach (var (source, repository) in _sources)
        {
            var mods = await listMods(repository).ConfigureAwait(false);
            foreach (var mod in mods)
            {
                if (seenIds.Add(mod.ModId))
                    results.Add(Tag(mod, source));
            }

            await AddClaimsAsync(repository, seenIds, cancellationToken).ConfigureAwait(false);
        }
        return results;
    }

    private async Task<ModVersionMetadata?> FirstReleaseAsync(
        string modId,
        Func<IModRepository, Task<ModVersionMetadata?>> getRelease,
        CancellationToken cancellationToken)
    {
        foreach (var (source, repository) in _sources)
        {
            var release = await getRelease(repository).ConfigureAwait(false);
            if (release is not null)
                return Tag(release, source);

            if (await ClaimsAsync(repository, modId, cancellationToken).ConfigureAwait(false))
                return null;
        }
        return null;
    }

    private static async Task AddClaimsAsync(
        IModRepository repository,
        ISet<string> seenIds,
        CancellationToken cancellationToken)
    {
        if (repository is not IModIdClaimSource claimSource)
            return;

        var claims = await claimSource.GetClaimedModIdsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var claim in claims)
            seenIds.Add(claim);
    }

    private static async Task<bool> ClaimsAsync(
        IModRepository repository,
        string modId,
        CancellationToken cancellationToken)
    {
        if (repository is not IModIdClaimSource claimSource)
            return false;

        var claims = await claimSource.GetClaimedModIdsAsync(cancellationToken).ConfigureAwait(false);
        return claims.Contains(modId, ModIds.Comparer);
    }

    private static ModMetadata Tag(ModMetadata original, string source) => new(
        original.SpecVersion,
        original.ModId,
        source,
        original.Name,
        original.Authors,
        original.Abstract,
        original.License,
        original.Links,
        original.GameMin,
        original.Type,
        original.Tags,
        original.Description,
        original.Status,
        original.SupersededBy,
        original.Releases,
        original.GameMax,
        original.Os,
        original.Loader,
        original.Dependencies,
        original.Install,
        original.Provides);

    private static ModVersionMetadata Tag(ModVersionMetadata original, string source) => new(
        original.SpecVersion,
        original.ModId,
        original.Version,
        original.ReleaseStatus,
        original.ReleaseDate,
        original.GameMin,
        original.GameMinRevision,
        original.Download,
        original.InstallSizeBytes,
        original.Dependencies,
        original.Type,
        original.VersionScheme,
        original.GameMax,
        original.GameMaxRevision,
        original.Os,
        original.Install,
        original.Loader,
        original.Changelog,
        original.ChangelogText,
        original.Listing,
        original.Yanked,
        original.YankedReason,
        source,
        original.Manages);
}
