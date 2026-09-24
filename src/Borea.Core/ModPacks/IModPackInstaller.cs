using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.ModPacks;

public interface IModPackInstaller
{
    /// <summary>
    /// Plans the install like <see cref="InstallAsync"/> without a write, so a member the plan
    /// would write is <see cref="ModPackMemberStatus.NotAttempted"/>.
    /// </summary>
    Task<ModPackInstallResult> PlanAsync(ModPackInstallRequest request, CancellationToken cancellationToken = default);

    /// <param name="progress">The reports of each operation, numbered across every operation of the pack.</param>
    /// <param name="stop">
    /// Stops the operations at a safe point, under the rule of <see cref="InstallStop"/>. A member it kept from
    /// running is <see cref="ModPackMemberStatus.NotAttempted"/>, and the result is <see cref="ModPackInstallResult.IsStopped"/>.
    /// </param>
    /// <exception cref="InsufficientDiskSpaceException">The plan needs more room than the disk has, so no member was installed.</exception>
    Task<ModPackInstallResult> InstallAsync(ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plans the install into a new instance of that name without creating it. The result has an empty
    /// <see cref="ModPackInstallResult.InstanceId"/>, and <see cref="ModPackInstallRequest.InstanceId"/> is not read.
    /// </summary>
    Task<ModPackInstallResult> PlanNewAsync(string instanceName, ModPackInstallRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the instance only when the plan of <see cref="PlanNewAsync"/> can run, and then installs like <see cref="InstallAsync"/>.
    /// Otherwise it creates nothing and returns that plan.
    /// </summary>
    /// <exception cref="InsufficientDiskSpaceException">The plan needs more room than the disk has, so no instance was created.</exception>
    Task<ModPackInstallResult> CreateAndInstallAsync(string instanceName, ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default);
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
    Removed,
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

    /// <summary>A stop kept at least one member from running.</summary>
    public bool IsStopped { get; }

    /// <summary>The reasons that hold for the whole pack, a retraction that was not confirmed or the conflicts and open choices of a plan that is not ready.</summary>
    public IReadOnlyList<PlanningMessage> PackReasons { get; }

    /// <summary>The unresolved and failed members. Empty when <see cref="PackReasons"/> says why.</summary>
    public IReadOnlyList<ModPackBlocker> Blockers { get; }

    public ModPackInstallResult(Guid instanceId, InstallPlan? plan, IReadOnlyList<ModPackMemberResult> members, IReadOnlyList<PlanningMessage> warnings, bool isComplete, bool isStopped = false)
    {
        InstanceId = instanceId;
        Plan = plan;
        Members = members;
        Warnings = warnings;
        IsComplete = isComplete;
        IsStopped = isStopped;
        PackReasons = plan is { IsReady: false }
            ? [.. plan.Conflicts, .. plan.UnresolvedChoices]
            : [.. warnings.Where(warning => warning.Kind == PlanningMessageKind.RetractedPack)];
        Blockers = PackReasons.Count > 0
            ? []
            : members
                .Where(member => member.Status is ModPackMemberStatus.Unresolved or ModPackMemberStatus.Failed)
                .Select(member => new ModPackBlocker(member, member.Status == ModPackMemberStatus.Unresolved ? WarningOf(member) : null))
                .ToList();
    }

    /// <summary>
    /// What kept the pack from installing completely, in English for the CLI and the log. Without a blocker or a pack reason,
    /// the members that were not tried say why, unless a stop was requested.
    /// </summary>
    public string DescribeBlockers()
    {
        var reasons = Blockers
            .Select(blocker => $"{blocker.Member.ModId} {blocker.Member.Version}: {blocker.Member.Message}")
            .Concat(PackReasons.Select(message => $"{message.ModId}: {message.Message}"))
            .Distinct()
            .ToList();
        if (reasons.Count == 0 && !IsStopped)
            reasons = Members.Where(member => member.Status == ModPackMemberStatus.NotAttempted).Select(member => member.Message ?? string.Empty).Distinct().ToList();
        return string.Join(" ", reasons);
    }

    private PlanningMessage? WarningOf(ModPackMemberResult member) =>
        Warnings.FirstOrDefault(warning => (warning.Kind is PlanningMessageKind.UnlistedPin or PlanningMessageKind.YankedPin) && ModIds.Equals(warning.ModId, member.ModId));
}

/// <param name="Warning">The warning that says why an unresolved member is unresolved, or null for a failed member, whose message says why.</param>
public sealed record ModPackBlocker(ModPackMemberResult Member, PlanningMessage? Warning);
