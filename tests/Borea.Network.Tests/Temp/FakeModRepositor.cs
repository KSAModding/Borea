using Borea.Core.Mods;

namespace Borea.Network.Tests.Temp;

/// <summary>
/// In-memory IModRepository double for testing CompositeModRepository's
/// routing/merging behavior without a real network call.
/// </summary>
internal sealed class FakeModRepository : IModRepository
{
    private readonly List<ModMetadata> _listings;
    private readonly List<ModVersionMetadata> _releases;

    public FakeModRepository(params ModMetadata[] listings)
        : this(listings, Array.Empty<ModVersionMetadata>())
    {
    }

    public FakeModRepository(IReadOnlyList<ModMetadata> listings, IReadOnlyList<ModVersionMetadata> releases)
    {
        _listings = listings.ToList();
        _releases = releases.ToList();
    }

    public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModMetadata>>(_listings);

    public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_releases
            .Where(r => ModIds.Equals(r.ModId, modId) && !r.Yanked)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault());

    public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
        Task.FromResult(_releases.FirstOrDefault(r => ModIds.Equals(r.ModId, modId) && r.Version.Equals(version)));

    public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModVersion>>(_releases
            .Where(r => ModIds.Equals(r.ModId, modId))
            .Select(r => r.Version)
            .OrderDescending()
            .ToList());

    public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModMetadata>>(_listings.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());
}
