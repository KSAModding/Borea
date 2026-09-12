using Borea.Core.Mods;

namespace Borea.Cli.Tests;

internal sealed class FakeModRepository : IModRepository
{
    public List<ModMetadata> Listings { get; } = new();

    public List<ModVersionMetadata> Releases { get; } = new();

    public Func<CancellationToken, Task<IReadOnlyList<ModMetadata>>>? AvailableMods { get; set; }

    public Func<string, CancellationToken, Task<IReadOnlyList<ModVersion>>>? AvailableVersions { get; set; }

    public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
        => AvailableMods?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<ModMetadata>>(Listings);

    public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default)
        => Task.FromResult(Releases
            .Where(release => ModIds.Equals(release.ModId, modId) && !release.Yanked)
            .OrderByDescending(release => release.Version)
            .FirstOrDefault());

    public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default)
        => Task.FromResult(Releases.FirstOrDefault(release =>
            ModIds.Equals(release.ModId, modId) && release.Version == version));

    public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
        => AvailableVersions?.Invoke(modId, cancellationToken) ?? Task.FromResult<IReadOnlyList<ModVersion>>(Releases
            .Where(release => ModIds.Equals(release.ModId, modId))
            .Select(release => release.Version)
            .Distinct()
            .OrderByDescending(version => version)
            .ToList());

    public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ModMetadata>>(Listings
            .Where(listing => listing.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList());
}
