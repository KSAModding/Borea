using Borea.Core.Mods;

namespace Borea.Network.Sources;

/// <summary>
/// Queries every registered source and merges results, tagging each listing
/// and release with its originating source via the Source property. Which
/// sources are active is entirely determined by what's registered at
/// construction.
/// </summary>
public sealed class CompositeModRepository : IModRepository
{
    private readonly IReadOnlyDictionary<string, IModRepository> _sources;

    public CompositeModRepository(IReadOnlyDictionary<string, IModRepository> sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    public async Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ModMetadata>();
        foreach (var (source, repository) in _sources)
        {
            var mods = await repository.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(mods.Select(m => Tag(m, source)));
        }
        return results;
    }

    public async Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default)
    {
        foreach (var (source, repository) in _sources)
        {
            var release = await repository.GetLatestReleaseAsync(modId, cancellationToken).ConfigureAwait(false);
            if (release is not null)
                return Tag(release, source);
        }
        return null;
    }

    public async Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
    {
        foreach (var (source, repository) in _sources)
        {
            var release = await repository.GetReleaseAsync(modId, version, cancellationToken).ConfigureAwait(false);
            if (release is not null)
                return Tag(release, source);
        }
        return null;
    }

    public async Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
    {
        foreach (var repository in _sources.Values)
        {
            var versions = await repository.GetAvailableVersionsAsync(modId, cancellationToken).ConfigureAwait(false);
            if (versions.Count > 0)
                return versions;
        }
        return Array.Empty<ModVersion>();
    }

    public async Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<ModMetadata>();
        foreach (var (source, repository) in _sources)
        {
            var mods = await repository.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            results.AddRange(mods.Select(m => Tag(m, source)));
        }
        return results;
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
        original.Listing,
        original.Yanked,
        original.YankedReason,
        source);
}
