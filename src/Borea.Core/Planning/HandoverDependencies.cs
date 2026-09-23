using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Planning;

/// <summary>
/// What the release a foreign mod is recorded as declares and the instance does
/// not have. <see cref="Missing"/> holds the required entries, and
/// <see cref="NotInstalled"/> the optional, recommended and suggested ones, which
/// a handover only reports.
/// </summary>
public sealed record HandoverDependencies(
    IReadOnlyList<ModDependency> Missing,
    IReadOnlyList<ModDependency> NotInstalled)
{
    /// <summary>Installs what is missing. Null until <see cref="PlanAsync"/> ran, and when nothing is missing.</summary>
    public InstallPlan? Plan { get; init; }

    /// <summary>The mods the plan replaces although Borea does not own their folders, so the plan would fail.</summary>
    public IReadOnlyList<string> NotOwned { get; init; } = [];

    public bool CanInstallMissing => Plan is { IsReady: true, Operations.Count: > 0 } && NotOwned.Count == 0;

    public static HandoverDependencies Check(Instance instance, InstalledMod mod)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(mod);

        var evaluations = new ModDependencyResolver().Evaluate(instance, mod.Metadata);
        return new HandoverDependencies(
            evaluations
                .Where(evaluation => evaluation.Outcome == DependencyOutcome.Install)
                .Select(evaluation => evaluation.Dependency)
                .ToList(),
            evaluations
                .Where(evaluation => evaluation.Outcome is DependencyOutcome.SelectByDefault or DependencyOutcome.Offer)
                .Select(evaluation => evaluation.Dependency)
                .ToList());
    }

    /// <summary>
    /// Plans the recorded release as an exact request. The instance already
    /// holds that release, so the plan installs only what the release needs.
    /// </summary>
    public async Task<HandoverDependencies> PlanAsync(
        IInstallPlanner planner,
        Instance instance,
        InstalledMod mod,
        IModRepository repository,
        GameVersion? gameVersion,
        OsPlatform? targetPlatform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(mod);
        if (Missing.Count == 0)
            return this;

        var request = new InstallPlanningRequest(instance, [new RequestedMod(mod.Metadata, mod.Reason)], repository, gameVersion, targetPlatform);
        var plan = await planner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        var notOwned = plan.Operations
            .Where(operation => instance.Mods.Any(installed => ModIds.Equals(installed.ModId, operation.Release.ModId) && !installed.CanDeleteFiles))
            .Select(operation => operation.Release.ModId)
            .ToList();
        return this with { Plan = plan, NotOwned = notOwned };
    }
}
