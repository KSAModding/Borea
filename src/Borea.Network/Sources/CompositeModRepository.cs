using Borea.Core.Mods;

namespace Borea.Network.Sources;

/// <summary>
/// Queries every registered source and merges results, tagging each
/// ModMetadata with its originating source via ModMetadata.Source. Which
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

    public async Task<ModMetadata?> GetLatestAsync(string modId, CancellationToken cancellationToken = default)
    {
        foreach (var (source, repository) in _sources)
        {
            var result = await repository.GetLatestAsync(modId, cancellationToken).ConfigureAwait(false);
            if (result is not null)
                return Tag(result, source);
        }
        return null;
    }

    public async Task<ModMetadata?> GetVersionAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
    {
        foreach (var (source, repository) in _sources)
        {
            var result = await repository.GetVersionAsync(modId, version, cancellationToken).ConfigureAwait(false);
            if (result is not null)
                return Tag(result, source);
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
        original.ModId, source, original.Name, original.Author, original.Version, original.BuiltForGameVersion,
        original.Description, original.ReleasedAt, original.FileSizeBytes, original.Dependencies,
        original.Tags, original.HomepageUrl, original.ChangeLog);
}