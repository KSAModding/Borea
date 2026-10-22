using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Planning;

/// <param name="Channel">The release channel of this plan. Null means the planner's channel.</param>
public sealed record InstallPlanningRequest(
    Instance Instance,
    IReadOnlyList<RequestedMod> Requested,
    IModRepository Repository,
    GameVersion? GameVersion = null,
    OsPlatform? TargetPlatform = null,
    IReadOnlySet<string>? Recommended = null,
    IReadOnlyDictionary<string, string>? Alternatives = null,
    ReleaseChannel? Channel = null)
{
    /// <summary>
    /// The instance before a run whose <see cref="Instance"/> is a draft without the mods the run removes, as a pack
    /// update plans. Null when the run removes nothing.
    /// </summary>
    public Instance? Current { get; init; }

    /// <summary>The listings of the vehicles and saves that the same run replaces, so their records do not count.</summary>
    public IReadOnlySet<string>? ReplacedContent { get; init; }
}
