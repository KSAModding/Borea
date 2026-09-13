using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Planning;

/// <summary>
/// The one <see cref="IInstallPlanExecutor"/>, shared by the CLI and the App so
/// that both write a plan the same way.
/// </summary>
public sealed class InstallPlanExecutor : IInstallPlanExecutor
{
    private readonly IInstanceRepository _instances;
    private readonly IModInstaller _installer;
    private readonly IModReplacer _replacer;

    public InstallPlanExecutor(IInstanceRepository instances, IModInstaller installer, IModReplacer replacer)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _replacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
    }

    public async Task ExecuteAsync(
        InstallPlan plan,
        bool enable,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsReady)
            throw new InvalidOperationException("The install plan has unresolved choices or conflicts.");

        var fresh = await _instances.GetByIdAsync(plan.InstanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{plan.InstanceId}' no longer exists.");
        if (!plan.InstanceState.Matches(fresh))
            throw new InvalidOperationException("The instance changed after Borea planned the operation.");

        var expectedState = plan.InstanceState;
        var step = 0;
        foreach (var operation in plan.Operations)
        {
            var operationProgress = InPlan(progress, ++step, plan.Operations.Count);
            fresh = await _instances.GetByIdAsync(plan.InstanceId).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Instance '{plan.InstanceId}' no longer exists.");
            if (!expectedState.Matches(fresh))
                throw new InvalidOperationException("The instance changed while Borea executed the operation.");

            var current = fresh.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, operation.Release.ModId));
            if (current is null)
            {
                var result = await _installer.InstallGuardedAsync(plan.InstanceId, operation.Release, operation.Reason, enable, expectedState, operationProgress, cancellationToken).ConfigureAwait(false);
                expectedState = result.State;
            }
            else
            {
                var result = await _replacer.ReplaceGuardedAsync(plan.InstanceId, current, operation.Release, expectedState, operationProgress, cancellationToken).ConfigureAwait(false);
                expectedState = result.State;
            }
        }
    }

    private static IProgress<InstallProgress>? InPlan(IProgress<InstallProgress>? progress, int step, int stepCount) =>
        progress is null
            ? null
            : new SynchronousProgress<InstallProgress>(value => progress.Report(value with { Step = step, StepCount = stepCount }));
}
