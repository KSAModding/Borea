using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.ModPacks;

/// <summary>Moves an instance that was created from a pack to a newer version of that pack.</summary>
public interface IModPackUpdater
{
    /// <summary>Plans the update like <see cref="UpdateAsync"/> without a write.</summary>
    Task<ModPackUpdateResult> PlanAsync(ModPackUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the mods the plan removes, then installs like a pack install. Only a complete update names
    /// the new pack version in the instance source and makes the kept mods normal mods of the instance.
    /// </summary>
    /// <param name="stop">Stops at a safe point, under the rule of <see cref="InstallStop"/>.</param>
    Task<ModPackUpdateResult> UpdateAsync(ModPackUpdateRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default);
}

/// <param name="Pack">The pack version to update to. It must be newer than the version the instance names.</param>
public sealed record ModPackUpdateRequest(
    Guid InstanceId,
    ModPackResult Pack,
    IModRepository Repository,
    GameVersion? GameVersion = null,
    OsPlatform? TargetPlatform = null,
    IReadOnlySet<string>? Recommended = null,
    IReadOnlyDictionary<string, string>? Alternatives = null,
    IReadOnlySet<string>? ProceedWithYankedMembers = null,
    bool Enable = true);

public enum ModPackChangeKind
{
    Add,
    Change,
    Remove,

    /// <summary>The new version no longer pins the mod, but it stays as a normal mod of the instance.</summary>
    Keep,
}

/// <param name="From">The installed version, or null for an added mod.</param>
/// <param name="To">The version after the update, or null for a removed mod. A kept mod can change version when a new pin needs it.</param>
public sealed record ModPackChange(string ModId, ModPackChangeKind Kind, ModVersion? From, ModVersion? To);

public sealed class ModPackUpdateResult
{
    public Guid InstanceId { get; }

    public ModVersion CurrentVersion { get; }

    public ModPackMetadata Target { get; }

    public IReadOnlyList<ModPackChange> Changes { get; }

    public InstallPlan? Plan { get; }

    public IReadOnlyList<ModPackMemberResult> Members { get; }

    public IReadOnlyList<PlanningMessage> Warnings { get; }

    /// <summary>Every step completed, and the instance source names <see cref="Target"/>.</summary>
    public bool IsComplete { get; }

    public bool IsStopped { get; }

    /// <summary>The plan can run without a choice or a confirmation that the request does not carry.</summary>
    public bool CanRun => Plan is { IsReady: true } && Members.All(member => member.Status != ModPackMemberStatus.Unresolved);

    /// <summary>
    /// The mods the update removes that it has not removed, because it stopped or did not finish.
    /// A plan that never got as far as running names none of them, because nothing about the instance changed.
    /// </summary>
    public IReadOnlyList<string> StillInstalled => !CanRun ? [] : Changes
        .Where(change => change.Kind == ModPackChangeKind.Remove)
        .Select(change => change.ModId)
        .Where(modId => !Members.Any(member => member.Status == ModPackMemberStatus.Removed && ModIds.Equals(member.ModId, modId)))
        .ToList();

    public ModPackUpdateResult(Guid instanceId, ModVersion currentVersion, ModPackMetadata target, IReadOnlyList<ModPackChange> changes, InstallPlan? plan, IReadOnlyList<ModPackMemberResult> members, IReadOnlyList<PlanningMessage> warnings, bool isComplete, bool isStopped = false)
    {
        InstanceId = instanceId;
        CurrentVersion = currentVersion;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
        Plan = plan;
        Members = members ?? throw new ArgumentNullException(nameof(members));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        IsComplete = isComplete;
        IsStopped = isStopped;
    }
}
