using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Core.Planning;

public enum PlanningMessageKind
{
    ForeignOwned,
    ExactPinConflict,
    OutsideChannel,
    MissingRequest,
    ExactPin,
    Pinned,
    PinnedDependency,
    ProposedConflict,
    Yanked,
    YankedPin,
    Unavailable,
    ReleaseChannel,
    Incompatible,
    Compatibility,
    Platform,
    PlatformUnknown,
    UnknownDependency,
    DependencyConflict,
    ForeignConflictUnknown,
    ForeignConflict,
    ForeignVersionUnknown,
    UnsatisfiedDependency,
    AlternativeChoice,
    InvalidAlternative,
    ForeignAlternativeUnknown,
    UnsatisfiedAlternative,
    RetainedAlternative,
    RetainedDependent,
    RetainedConflict,
    RetainedUnsatisfied,
    SearchLimit,
    RetractedPack,
    UnlistedPin,
    AboveGameMax,

    /// <summary>A required mod of a vehicle that Borea cannot install or enable. A warning, so the player can go on without it.</summary>
    MissingForVehicle,

    /// <summary>A required mod of a save that Borea cannot install or enable. A warning, so the player can go on without it.</summary>
    MissingForSave,

    /// <summary>
    /// The plan takes a mod out of the range that a recorded vehicle needs. <see cref="PlanningMessage.Value"/> is the folder of the vehicle.
    /// A warning only, because the game refuses the vehicle and changes nothing else.
    /// </summary>
    BreaksVehicle,

    /// <summary>The plan takes a mod out of the range that a recorded save needs. <see cref="PlanningMessage.Value"/> is the folder of the save.</summary>
    BreaksSave,
}

/// <summary>
/// A warning, an open choice or a conflict about one mod, as a kind and the values that kind names.
/// </summary>
public sealed record PlanningMessage(string ModId, PlanningMessageKind Kind)
{
    public ModDependency? Dependency { get; init; }

    public ModVersion? Version { get; init; }

    public ModVersion? OtherVersion { get; init; }

    public ReleaseStatus? Status { get; init; }

    public ReleaseChannel? Channel { get; init; }

    public GameCompatibility? Compatibility { get; init; }

    public OsPlatform? Platform { get; init; }

    /// <summary>The author's reason for a yank or a retraction, an unknown platform, an alternative's mod id, the game build a release needs, the newest game build a release supports, the pinned mod a release needs, the code of the reason a required mod of a vehicle or save is missing, or the folder of a vehicle or save a plan breaks.</summary>
    public string? Value { get; init; }

    public int? Limit { get; init; }

    /// <summary>When the download of the release went from its host (RFC 0078).</summary>
    public DateTimeOffset? Since { get; init; }

    public string Code => Kind switch
    {
        PlanningMessageKind.ForeignOwned => "foreign-owned",
        PlanningMessageKind.ExactPinConflict => "exact-pin-conflict",
        PlanningMessageKind.OutsideChannel => "outside-channel",
        PlanningMessageKind.MissingRequest => "missing-request",
        PlanningMessageKind.ExactPin => "exact-pin",
        PlanningMessageKind.Pinned => "pinned",
        PlanningMessageKind.PinnedDependency => "pinned-dependency",
        PlanningMessageKind.ProposedConflict => "proposed-conflict",
        PlanningMessageKind.Yanked or PlanningMessageKind.YankedPin => "yanked",
        PlanningMessageKind.Unavailable => "unavailable",
        PlanningMessageKind.ReleaseChannel => "release-channel",
        PlanningMessageKind.Incompatible => "incompatible",
        PlanningMessageKind.AboveGameMax => "above-game-max",
        PlanningMessageKind.Compatibility => "compatibility",
        PlanningMessageKind.Platform => "platform",
        PlanningMessageKind.PlatformUnknown => "platform-unknown",
        PlanningMessageKind.UnknownDependency => "unknown-dependency",
        PlanningMessageKind.DependencyConflict => "dependency-conflict",
        PlanningMessageKind.ForeignConflictUnknown => "foreign-conflict-unknown",
        PlanningMessageKind.ForeignConflict => "foreign-conflict",
        PlanningMessageKind.ForeignVersionUnknown => "foreign-version-unknown",
        PlanningMessageKind.UnsatisfiedDependency => "unsatisfied-dependency",
        PlanningMessageKind.AlternativeChoice => "alternative-choice",
        PlanningMessageKind.InvalidAlternative => "invalid-alternative",
        PlanningMessageKind.ForeignAlternativeUnknown => "foreign-alternative-unknown",
        PlanningMessageKind.UnsatisfiedAlternative => "unsatisfied-alternative",
        PlanningMessageKind.RetainedAlternative => "retained-alternative",
        PlanningMessageKind.RetainedDependent => "retained-dependent",
        PlanningMessageKind.RetainedConflict => "retained-conflict",
        PlanningMessageKind.RetainedUnsatisfied => "retained-unsatisfied",
        PlanningMessageKind.SearchLimit => "search-limit",
        PlanningMessageKind.RetractedPack => "retracted-pack",
        PlanningMessageKind.UnlistedPin => "unlisted-pin",
        PlanningMessageKind.MissingForVehicle => "missing-for-vehicle",
        PlanningMessageKind.MissingForSave => "missing-for-save",
        PlanningMessageKind.BreaksVehicle => "breaks-vehicle",
        PlanningMessageKind.BreaksSave => "breaks-save",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "The planning message kind is not defined."),
    };

    /// <summary>The English text that the CLI and the log print.</summary>
    public string Message => Kind switch
    {
        PlanningMessageKind.ForeignOwned => "A managed install cannot replace foreign content.",
        PlanningMessageKind.ExactPinConflict => $"Exact versions {Version} and {OtherVersion} were both requested.",
        PlanningMessageKind.OutsideChannel => $"No release of {ModId} in the {Channel?.ToName()} channel is available.",
        PlanningMessageKind.MissingRequest => "No release was selected for the request.",
        PlanningMessageKind.ExactPin => $"Exact version {Version} was not selected.",
        PlanningMessageKind.Pinned => $"The mod is pinned at {Version} in this instance, so Borea does not change it to {OtherVersion}. Unpin it first.",
        PlanningMessageKind.PinnedDependency => $"{Dependency}: {Value} is pinned at {Version} in this instance.",
        PlanningMessageKind.Yanked => Value ?? "The selected release is yanked.",
        PlanningMessageKind.YankedPin => Value ?? "The exact pinned release is yanked.",
        PlanningMessageKind.Unavailable => $"The download of {ModId} {Version} is gone from its host since {Since:yyyy-MM-dd}, so Borea can install it only from a mirror or a copy it already has.",
        PlanningMessageKind.ReleaseChannel => $"Release {Version} has the release status {StatusName(Status)}, which the {Channel?.ToName()} channel does not offer.",
        PlanningMessageKind.Incompatible => "The release is incompatible with the target game.",
        PlanningMessageKind.AboveGameMax => $"The release supports the game only up to {Value}, and the target game is newer.",
        PlanningMessageKind.Compatibility => $"Game compatibility is {Compatibility}.",
        PlanningMessageKind.Platform => $"The release does not list {Platform} as a supported platform.",
        PlanningMessageKind.PlatformUnknown => $"The release contains the unknown platform '{Value}'.",
        PlanningMessageKind.AlternativeChoice => $"Select one alternative for {Dependency}.",
        PlanningMessageKind.InvalidAlternative => $"'{Value}' is not an available alternative.",
        PlanningMessageKind.ForeignAlternativeUnknown or PlanningMessageKind.UnsatisfiedAlternative => Value ?? $"{Dependency}",
        PlanningMessageKind.SearchLimit => $"Planning exceeded the deterministic limit of {Limit} candidate states.",
        PlanningMessageKind.RetractedPack => Value ?? "The selected pack version is retracted.",
        PlanningMessageKind.UnlistedPin => "The exact pinned release is not listed and cannot be installed by Borea.",
        PlanningMessageKind.MissingForVehicle => $"{Dependency} cannot be installed or enabled, so the game refuses the vehicle file.",
        PlanningMessageKind.MissingForSave => $"{Dependency} cannot be installed or enabled, so the game leaves out every vehicle that needs it, and saving again loses those vehicles.",
        PlanningMessageKind.BreaksVehicle => $"{ModId} {Version} does not meet the {Dependency} of the vehicle {Value}, so the game refuses that vehicle.",
        PlanningMessageKind.BreaksSave => $"{ModId} {Version} does not meet the {Dependency} of the save {Value}, so the game leaves out every vehicle of that save that needs it, and saving again loses those vehicles.",
        _ => $"{Dependency}",
    };

    private static string StatusName(ReleaseStatus? status) => status switch
    {
        ReleaseStatus.Stable => "stable",
        ReleaseStatus.Testing => "testing",
        ReleaseStatus.Dev => "dev",
        _ => "unknown",
    };
}
