using Borea.Core.Mods;

namespace Borea.Core.Planning;

public interface IInstallPlanner
{
    Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default);
}

public sealed record RequestedMod(ModVersionMetadata Release, InstallReason Reason, bool Exact = true);
