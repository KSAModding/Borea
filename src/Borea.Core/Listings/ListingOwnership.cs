namespace Borea.Core.Listings;

public enum ListingOwnershipState
{
    /// <summary>A validated pull request of the signed-in account merges itself.</summary>
    Verified,

    /// <summary>A steward has to accept the pull request.</summary>
    NotVerified,

    CouldNotEvaluate,
}

public enum ListingOwnershipProof
{
    Owner,
    Topic,
    MarkerFile,
}

/// <summary>Why the proof is missing, which decides the one step that fixes it.</summary>
public enum ListingOwnershipProblem
{
    NoProof,
    NoHost,
    RepositoryMissing,
    RepositoryFork,
    RepositoryRenamed,
    SpaceDockModUnusable,
    SpaceDockNoSourceLink,
    PullRequestHasOtherFiles,
}

/// <param name="Repository">The GitHub repository the proof or the fix is about.</param>
/// <param name="SpaceDockMod">The SpaceDock mod whose source code link is missing or unusable.</param>
/// <param name="RenamedTo">The name GitHub answers with for a renamed repository.</param>
/// <param name="PullRequest">The number of the author's open pull request that the listing goes into.</param>
public sealed record ListingOwnership(
    ListingOwnershipState State,
    ListingOwnershipProof? Proof = null,
    ListingOwnershipProblem? Problem = null,
    string? Repository = null,
    string? SpaceDockMod = null,
    string? RenamedTo = null,
    int? PullRequest = null)
{
    public const string MarkerPath = ".github/ksa-content-index.toml";

    public static ListingOwnership Unknown { get; } = new(ListingOwnershipState.CouldNotEvaluate);

    /// <summary>The topic that proves control for <paramref name="login"/>.</summary>
    public static string TopicFor(string login) => "ksa-index-" + login.ToLowerInvariant();
}
