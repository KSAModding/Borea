using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.ModPacks;

public interface IModPackInstaller
{
    Task<ModPackInstallResult> InstallAsync(ModPackInstallRequest request, CancellationToken cancellationToken = default);

    Task<ModPackInstallResult> CreateAndInstallAsync(string instanceName, ModPackInstallRequest request, CancellationToken cancellationToken = default);
}

public sealed record ModPackInstallRequest(
    Guid InstanceId,
    ModPackResult Pack,
    IModRepository Repository,
    GameVersion? GameVersion = null,
    OsPlatform? TargetPlatform = null,
    IReadOnlySet<string>? Recommended = null,
    IReadOnlyDictionary<string, string>? Alternatives = null,
    bool ProceedWithRetractedPack = false,
    IReadOnlySet<string>? ProceedWithYankedMembers = null,
    bool Enable = true);

public enum ModPackMemberStatus
{
    Installed,
    Replaced,
    AlreadyInstalled,
    Unresolved,
    Failed,
    NotAttempted,
}

public sealed record ModPackMemberResult(
    string ModId,
    ModVersion Version,
    InstallReason Reason,
    ModPackMemberStatus Status,
    string? Message = null,
    string? Location = null);

public sealed class ModPackInstallResult
{
    public Guid InstanceId { get; }
    public InstallPlan? Plan { get; }
    public IReadOnlyList<ModPackMemberResult> Members { get; }
    public IReadOnlyList<PlanningMessage> Warnings { get; }
    public bool IsComplete { get; }

    public ModPackInstallResult(Guid instanceId, InstallPlan? plan, IReadOnlyList<ModPackMemberResult> members, IReadOnlyList<PlanningMessage> warnings, bool isComplete)
    {
        InstanceId = instanceId;
        Plan = plan;
        Members = members;
        Warnings = warnings;
        IsComplete = isComplete;
    }
}
