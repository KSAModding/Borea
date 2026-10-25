using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.UserContent;

/// <param name="Name">A new name the player chose, or null for the name of the release, or for an update the name the item has.</param>
/// <param name="Channel">The release channel of the mods it needs. Null means the planner's channel.</param>
public sealed record UserContentRequest(
    Instance Instance,
    ModVersionMetadata Release,
    IModRepository Repository,
    GameVersion? GameVersion = null,
    OsPlatform? TargetPlatform = null,
    IReadOnlySet<string>? Recommended = null,
    IReadOnlyDictionary<string, string>? Alternatives = null,
    ReleaseChannel? Channel = null,
    string? Name = null);

/// <summary>
/// The install or update of one vehicle or save with the mods it needs (RFC 0085): the mods install
/// first and enabled, and the folder moves into place last.
/// </summary>
public sealed class UserContentPlan
{
    public ModVersionMetadata Release { get; }

    /// <summary>The record an update replaces, or null for an install.</summary>
    public InstalledContentRecord? Installed { get; }

    /// <summary>The mods, planned like the dependencies of a mod. The vehicle or save itself is no operation of it.</summary>
    public InstallPlan Mods { get; }

    /// <summary>The mods the item needs. Each one is enabled after the mods install.</summary>
    public IReadOnlyList<string> Needed { get; }

    /// <summary>The installed mods of <see cref="Needed"/> that the game does not load now, so the plan enables them.</summary>
    public IReadOnlyList<string> Enables { get; }

    /// <summary>What the folder step would do: <see cref="UserContentOutcome.Ready"/>, or the outcome that stops it.</summary>
    public UserContentResult Check { get; }

    public bool DiscardChanges { get; }

    /// <summary>The required mods that cannot be installed or enabled, and what the game does without them.</summary>
    public IReadOnlyList<PlanningMessage> Missing => Mods.Warnings.Where(warning => warning.Kind is PlanningMessageKind.MissingForVehicle or PlanningMessageKind.MissingForSave).ToList();

    /// <summary>Whether it can run. A plan with <see cref="Missing"/> runs only when the player goes on without those mods.</summary>
    public bool IsReady => Mods.IsReady && Check.Outcome == UserContentOutcome.Ready;

    public UserContentPlan(ModVersionMetadata release, InstalledContentRecord? installed, InstallPlan mods, IReadOnlyList<string> needed, IReadOnlyList<string> enables, UserContentResult check, bool discardChanges)
    {
        Release = release ?? throw new ArgumentNullException(nameof(release));
        Installed = installed;
        Mods = mods ?? throw new ArgumentNullException(nameof(mods));
        Needed = needed ?? throw new ArgumentNullException(nameof(needed));
        Enables = enables ?? throw new ArgumentNullException(nameof(enables));
        Check = check ?? throw new ArgumentNullException(nameof(check));
        DiscardChanges = discardChanges;
    }
}
