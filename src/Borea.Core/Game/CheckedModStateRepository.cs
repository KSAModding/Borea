using Borea.Core.State;

namespace Borea.Core.Game;

/// <summary>
/// An <see cref="IModStateRepository"/> that refuses to write while the game
/// does not have the shape Borea expects. Reads pass through, so the App and
/// the command line can still show what the manifest holds.
/// </summary>
public sealed class CheckedModStateRepository : IModStateRepository
{
    private readonly IGameShapeCheck _shape;

    public IModStateRepository Inner { get; }

    public CheckedModStateRepository(IModStateRepository inner, IGameShapeCheck shape)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _shape = shape ?? throw new ArgumentNullException(nameof(shape));
    }

    public Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => Inner.GetEntriesAsync(instanceId, cancellationToken);

    public Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
        => Inner.IsActiveAsync(instanceId, modId, cancellationToken);

    public Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => Inner.GetAllActiveModIdsAsync(instanceId, cancellationToken);

    public async Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.AddEntryAsync(instanceId, modId, enabled, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.SetActiveAsync(instanceId, modId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.SetInactiveAsync(instanceId, modId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.ReorderAsync(instanceId, modIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PutGameContentFirstAsync(Guid instanceId, string gameDirectory, CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.PutGameContentFirstAsync(instanceId, gameDirectory, cancellationToken).ConfigureAwait(false);
    }
}
