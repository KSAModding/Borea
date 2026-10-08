namespace Borea.Core.Listings;

public enum ListingOwnershipState
{
    /// <summary>A validated pull request of the account merges itself.</summary>
    Verified,

    /// <summary>A steward has to accept the pull request.</summary>
    NotVerified,

    CouldNotEvaluate,

    /// <summary>
    /// The pack id is free, so the pull request claims it for the signed-in account. It never merges itself, because a steward
    /// accepts every first claim after the checks pass.
    /// </summary>
    FirstClaim,
}

public enum ListingOwnershipProof
{
    Owner,
    Topic,
    MarkerFile,

    /// <summary>The owner record of the pack on main names the signed-in account.</summary>
    PackOwner,
}

/// <summary>Why the proof is missing, which decides the one step that fixes it.</summary>
public enum ListingOwnershipProblem
{
    NoProof,
    NoHost,
    RepositoryMissing,

    /// <summary>The repository is a fork without the owner or the topic proof. Its marker file proves nothing, because a fork inherits it.</summary>
    RepositoryFork,
    RepositoryRenamed,
    SpaceDockModUnusable,
    SpaceDockNoSourceLink,
    PullRequestHasOtherFiles,

    /// <summary>The owner record of the pack on main names another account, which <see cref="ListingOwnership.PackOwner"/> holds.</summary>
    PackOwnedByOther,

    /// <summary>Another listing or pack holds the pack id in another letter case, which <see cref="ListingOwnership.TakenBy"/> holds.</summary>
    PackIdTaken,
}

/// <param name="Repository">The GitHub repository the proof or the fix is about.</param>
/// <param name="SpaceDockMod">The SpaceDock mod whose source code link is missing or unusable.</param>
/// <param name="RenamedTo">The name GitHub answers with for a renamed repository.</param>
/// <param name="PullRequest">The number of the author's open pull request that the listing goes into.</param>
/// <param name="PackOwner">The login that the owner record of the pack on main names.</param>
/// <param name="TakenBy">The id in the content index that already holds the pack id.</param>
/// <param name="Claim">For a first claim, the owner record of the signed-in account that the pull request adds.</param>
/// <param name="ForumsThread">The forums thread of the listing, as the listed document names it for an edit.</param>
public sealed record ListingOwnership(
    ListingOwnershipState State,
    ListingOwnershipProof? Proof = null,
    ListingOwnershipProblem? Problem = null,
    string? Repository = null,
    string? SpaceDockMod = null,
    string? RenamedTo = null,
    int? PullRequest = null,
    string? PackOwner = null,
    string? TakenBy = null,
    ListingPackOwner? Claim = null,
    Uri? ForumsThread = null)
{
    public const string MarkerPath = ".github/ksa-content-index.toml";

    /// <summary>The GitHub page of <see cref="Repository"/>, or null when it names no repository.</summary>
    public Uri? RepositoryUrl =>
        ListingAuthority.GitHubRepositoryOf("https://github.com/" + Repository) is { } name && string.Equals(name, Repository, StringComparison.Ordinal)
            ? new Uri("https://github.com/" + name)
            : null;

    /// <summary>The SpaceDock page of <see cref="SpaceDockMod"/>, or null when it names no mod.</summary>
    public Uri? SpaceDockModUrl =>
        SpaceDockMod is { Length: > 0 } mod && mod.All(char.IsAsciiDigit) ? new Uri("https://spacedock.info/mod/" + mod) : null;

    /// <summary>
    /// Whether the proof, or the problem, is on <see cref="Repository"/> because the source code link of
    /// <see cref="SpaceDockMod"/> names it (RFC 0079).
    /// </summary>
    public bool IsThroughSpaceDockLink =>
        SpaceDockMod is not null
        && Repository is not null
        && (State == ListingOwnershipState.Verified
            || Problem is ListingOwnershipProblem.NoProof
                or ListingOwnershipProblem.RepositoryFork
                or ListingOwnershipProblem.RepositoryMissing
                or ListingOwnershipProblem.RepositoryRenamed);

    public static ListingOwnership Unknown { get; } = new(ListingOwnershipState.CouldNotEvaluate);

    /// <summary>The topic that proves control for <paramref name="login"/>.</summary>
    public static string TopicFor(string login) => "ksa-index-" + login.ToLowerInvariant();
}
