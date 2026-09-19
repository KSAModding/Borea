using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// SpaceDock without a request. A lookup that SpaceDock would answer with a request fails,
/// and a mod id that SpaceDock cannot resolve is unknown, as it is to SpaceDock.
/// </summary>
public sealed class OfflineSpaceDockModRepository : IModRepository
{
    private readonly SpaceDockResolver _resolver;

    public OfflineSpaceDockModRepository(SpaceDockResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<ModMetadata>>(Offline());

    public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default) =>
        Lookup<ModMetadata?>(modId, null);

    public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
        Lookup<ModVersionMetadata?>(modId, null);

    public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
        Lookup<ModVersionMetadata?>(modId, null);

    public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) =>
        Lookup<IReadOnlyList<ModVersion>>(modId, []);

    public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<ModMetadata>>(Offline());

    private Task<T> Lookup<T>(string modId, T unknown) =>
        _resolver.TryResolveId(modId, out _) ? Task.FromException<T>(Offline()) : Task.FromResult(unknown);

    private static NotSupportedException Offline() => new("SpaceDock can only answer with a network request.");
}
