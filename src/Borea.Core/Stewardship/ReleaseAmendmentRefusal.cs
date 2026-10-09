namespace Borea.Core.Stewardship;

/// <summary>Why an amendment of release files is refused before anything is written, as tools/amend.py of content-index-releases would refuse it.</summary>
public enum ReleaseAmendmentRefusal
{
    /// <summary>The change names nothing, has a reason without a yank, or has a value that does not parse.</summary>
    InvalidChange,

    /// <summary>A game_max names a month that is not over, so the month has no last build yet.</summary>
    MonthNotOver,

    /// <summary>A game bound names a month with no build in game-versions.json.</summary>
    UnknownMonth,

    /// <summary>A selected version has no release file, or no release is at or below the version.</summary>
    UnknownRelease,

    /// <summary>A release file is no JSON object, has a file name that is no version, or has no game_min_revision, so the stamper did not write it.</summary>
    NotStamperFile,

    /// <summary>A loader bound is set on a release that states no loader.</summary>
    NoLoader,

    /// <summary>A dependency bound names a dependency that the release does not state.</summary>
    NoDependency,

    /// <summary>The change widens a release, which only the verified owner of the listing does (RFC 0079).</summary>
    Widens,

    /// <summary>The checks of content-index-releases would reject the file, because the change is outside the amendment class of RFC 0031.</summary>
    OutsideClass,

    /// <summary>The change removes an authored dependency entry, and the release archive could not be read to show that its mod.toml does not declare it.</summary>
    UnreadableArchive,
}

/// <param name="details">What is wrong, in the words of the checks of content-index-releases where they find it.</param>
public sealed class ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal refusal, IReadOnlyList<string> details)
    : Exception($"{refusal}: {string.Join(" ", details)}")
{
    public ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal refusal, string detail)
        : this(refusal, [detail])
    {
    }

    public ReleaseAmendmentRefusal Refusal { get; } = refusal;

    public IReadOnlyList<string> Details { get; } = details;
}
