using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Game;

/// <summary>
/// An <see cref="IModReplacer"/> that refuses while the game does not have the
/// shape Borea expects, so an update does not replace a working install.
/// </summary>
public sealed class CheckedModReplacer : IModReplacer
{
    private readonly IGameShapeCheck _shape;

    public IModReplacer Inner { get; }

    public CheckedModReplacer(IModReplacer inner, IGameShapeCheck shape)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _shape = shape ?? throw new ArgumentNullException(nameof(shape));
    }

    public async Task<ModReplacementResult> ReplaceAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.ReplaceAsync(instanceId, expectedCurrent, replacement, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GuardedModReplacementResult> ReplaceGuardedAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        InstallPlanningState expectedState,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.ReplaceGuardedAsync(instanceId, expectedCurrent, replacement, expectedState, progress, cancellationToken).ConfigureAwait(false);
    }
}
