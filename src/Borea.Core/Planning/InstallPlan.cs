using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Core.Planning;

public sealed record PlannedInstall(ModVersionMetadata Release, InstallReason Reason, GameCompatibility Compatibility, OsSupport? PlatformSupport);
public sealed record PlannedSelection(ModVersionMetadata Release, InstallReason Reason, bool IsAlreadyInstalled);
public sealed record PlanningMessage(string ModId, string Code, string Message);
public sealed record PlanningChoice(string Key, string OwnerModId, string Kind, IReadOnlyList<string> Options, string? Selected);

public sealed class InstallPlan
{
    public Guid InstanceId { get; }
    public InstallPlanningState InstanceState { get; }
    public IReadOnlyList<PlannedSelection> Selections { get; }
    public IReadOnlyList<PlannedInstall> Operations { get; }
    public IReadOnlyList<PlanningMessage> Warnings { get; }
    public IReadOnlyList<PlanningMessage> UnresolvedChoices { get; }
    public IReadOnlyList<PlanningMessage> Conflicts { get; }
    public IReadOnlyList<PlanningChoice> Choices { get; }
    public bool IsReady => UnresolvedChoices.Count == 0 && Conflicts.Count == 0;

    public InstallPlan(Guid instanceId, InstallPlanningState instanceState, IReadOnlyList<PlannedSelection> selections, IReadOnlyList<PlannedInstall> operations, IReadOnlyList<PlanningMessage> warnings, IReadOnlyList<PlanningMessage> unresolvedChoices, IReadOnlyList<PlanningMessage> conflicts, IReadOnlyList<PlanningChoice> choices)
    {
        InstanceId = instanceId;
        InstanceState = instanceState;
        Selections = selections;
        Operations = operations;
        Warnings = warnings;
        UnresolvedChoices = unresolvedChoices;
        Conflicts = conflicts;
        Choices = choices;
    }
}
