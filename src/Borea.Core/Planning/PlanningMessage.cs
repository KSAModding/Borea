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
    ProposedConflict,
    Yanked,
    YankedPin,
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

    /// <summary>The author's reason for a yank or a retraction, an unknown platform, an alternative's mod id, or the game build a release needs.</summary>
    public string? Value { get; init; }

    public int? Limit { get; init; }

    public string Code => Kind switch
    {
        PlanningMessageKind.ForeignOwned => "foreign-owned",
        PlanningMessageKind.ExactPinConflict => "exact-pin-conflict",
        PlanningMessageKind.OutsideChannel => "outside-channel",
        PlanningMessageKind.MissingRequest => "missing-request",
        PlanningMessageKind.ExactPin => "exact-pin",
        PlanningMessageKind.ProposedConflict => "proposed-conflict",
        PlanningMessageKind.Yanked or PlanningMessageKind.YankedPin => "yanked",
        PlanningMessageKind.ReleaseChannel => "release-channel",
        PlanningMessageKind.Incompatible => "incompatible",
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
        PlanningMessageKind.Yanked => Value ?? "The selected release is yanked.",
        PlanningMessageKind.YankedPin => Value ?? "The exact pinned release is yanked.",
        PlanningMessageKind.ReleaseChannel => $"Release {Version} has the release status {StatusName(Status)}, which the {Channel?.ToName()} channel does not offer.",
        PlanningMessageKind.Incompatible => "The release is incompatible with the target game.",
        PlanningMessageKind.Compatibility => $"Game compatibility is {Compatibility}.",
        PlanningMessageKind.Platform => $"The release does not list {Platform} as a supported platform.",
        PlanningMessageKind.PlatformUnknown => $"The release contains the unknown platform '{Value}'.",
        PlanningMessageKind.AlternativeChoice => $"Select one alternative for {Dependency}.",
        PlanningMessageKind.InvalidAlternative => $"'{Value}' is not an available alternative.",
        PlanningMessageKind.ForeignAlternativeUnknown or PlanningMessageKind.UnsatisfiedAlternative => Value ?? $"{Dependency}",
        PlanningMessageKind.SearchLimit => $"Planning exceeded the deterministic limit of {Limit} candidate states.",
        PlanningMessageKind.RetractedPack => Value ?? "The selected pack version is retracted.",
        PlanningMessageKind.UnlistedPin => "The exact pinned release is not listed and cannot be installed by Borea.",
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
