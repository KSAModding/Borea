using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Game;

/// <summary>
/// An <see cref="IModInstaller"/> that refuses while the game does not have the
/// shape Borea expects, before anything is downloaded or unpacked.
/// </summary>
public sealed class CheckedModInstaller : IModInstaller
{
    private readonly IGameShapeCheck _shape;

    public IModInstaller Inner { get; }

    public CheckedModInstaller(IModInstaller inner, IGameShapeCheck shape)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _shape = shape ?? throw new ArgumentNullException(nameof(shape));
    }

    public async Task<InstallResult> InstallAsync(
        Guid instanceId,
        ModVersionMetadata release,
        InstallReason reason,
        bool enable,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.InstallAsync(instanceId, release, reason, enable, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GuardedInstallResult> InstallGuardedAsync(
        Guid instanceId,
        ModVersionMetadata release,
        InstallReason reason,
        bool enable,
        InstallPlanningState expectedState,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await GameShapeGate.RequireAsync(_shape, instanceId, cancellationToken).ConfigureAwait(false);
        return await Inner.InstallGuardedAsync(instanceId, release, reason, enable, expectedState, progress, cancellationToken).ConfigureAwait(false);
    }
}
