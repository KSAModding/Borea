using Borea.Core.Mods;

namespace Borea.Network.Tests.Temp;

/// <summary>
/// In-memory IModRepository double for testing CompositeModRepository's
/// routing/merging behavior without a real network call.
/// </summary>
internal sealed class FakeModRepository : IModRepository
{
    private readonly List<ModMetadata> _mods;

    public FakeModRepository(params ModMetadata[] mods) => _mods = mods.ToList();

    public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModMetadata>>(_mods);

    public Task<ModMetadata?> GetLatestAsync(string modId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_mods.FirstOrDefault(m => m.ModId == modId));

    public Task<ModMetadata?> GetVersionAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
        Task.FromResult(_mods.FirstOrDefault(m => m.ModId == modId && m.Version.Equals(version)));

    public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModVersion>>(_mods.Where(m => m.ModId == modId).Select(m => m.Version!).ToList().Where(v => v != null).Cast<ModVersion>().ToList());

    public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModMetadata>>(_mods.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());
}
