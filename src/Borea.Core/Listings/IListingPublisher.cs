namespace Borea.Core.Listings;

/// <summary>
/// Opens the pull request of one listing document in content-index from the signed-in GitHub account, and follows it.
/// The checks of content-index stay the authority on the verdict and on ownership.
/// </summary>
public interface IListingPublisher
{
    /// <summary>Which ownership proof the checks would find for the signed-in account. It reads only and never blocks.</summary>
    /// <param name="listed">The listed document on main that an edit changes, or null for a new listing.</param>
    /// <exception cref="ListingPublishException">Signed out, or GitHub refused the token.</exception>
    Task<ListingOwnership> CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the file to a branch of the author's fork and opens the pull request, or commits it to the
    /// author's open pull request that already changes the file. The author makes the fork and installs the App on it.
    /// </summary>
    /// <exception cref="ListingPublishException">A step failed. Nothing of the draft is lost.</exception>
    Task<ListingPullRequest> PublishAsync(ListingSubmission submission, IProgress<ListingPublishStep>? progress = null, CancellationToken cancellationToken = default);

    /// <exception cref="ListingPublishException">The status could not be read.</exception>
    Task<ListingPullRequestStatus> GetStatusAsync(int number, CancellationToken cancellationToken = default);
}

/// <param name="Text">The listing file, as the listing format writes it.</param>
/// <param name="IsEdit">Whether the file changes a listed document.</param>
public sealed record ListingSubmission(string Id, string Name, string Text, bool IsEdit)
{
    public string Path => $"{ListingDraft.ListingsFolder}/{Id}.toml";
}

public enum ListingPublishOutcome
{
    Opened,

    /// <summary>The file went to the author's open pull request as a new commit.</summary>
    Updated,

    /// <summary>The author's open pull request already has this file.</summary>
    Unchanged,
}

public sealed record ListingPullRequest(int Number, Uri Url, ListingPublishOutcome Outcome);

public enum ListingPublishStep
{
    Ownership,
    FindPullRequest,
    Fork,
    Branch,
    Commit,
    PullRequest,
    Status,
}

public enum ListingPullRequestState
{
    ChecksRunning,
    ValidatedMerging,
    WaitingForSteward,
    Rejected,
    CouldNotEvaluate,
    Merged,
    Closed,
}

/// <param name="Verdict">The comment of the checks as Markdown, without its marker, or null before they commented.</param>
public sealed record ListingPullRequestStatus(ListingPullRequestState State, string? Verdict)
{
    /// <summary>Nothing changes any more.</summary>
    public bool IsFinal => State is ListingPullRequestState.Merged or ListingPullRequestState.Closed;
}

public enum ListingPublishFailure
{
    SignedOut,
    RateLimited,
    NotFound,
    Refused,
    Forbidden,
    NetworkError,

    /// <summary>The author has no fork of content-index.</summary>
    NoFork,

    /// <summary>The Borea App is not installed on the author's fork, which <see cref="ListingPublishException.Detail"/> names.</summary>
    AppNotOnFork,

    /// <summary>The author's open pull request that changes the file does not come from the fork. <see cref="ListingPublishException.Detail"/> holds its number.</summary>
    PullRequestNotOnFork,

    /// <summary>The author's fork, which <see cref="ListingPublishException.Detail"/> names, has to be synced with content-index on GitHub first.</summary>
    ForkNeedsSync,

    NoChange,
    UnexpectedResponse,
}

/// <summary>A step of the pull request failed. The message never holds the token.</summary>
public sealed class ListingPublishException : Exception
{
    public ListingPublishException(ListingPublishFailure failure, ListingPublishStep step, string? detail = null, DateTimeOffset? retryAt = null, Exception? innerException = null, long? repositoryId = null)
        : base(Describe(failure, step, detail) + (innerException is ListingPublishException cause ? $" ({Describe(cause.Failure, null, cause.Detail)})" : string.Empty), innerException)
    {
        Failure = failure;
        Step = step;
        Detail = detail;
        RetryAt = retryAt;
        RepositoryId = repositoryId;
    }

    public ListingPublishFailure Failure { get; }

    public ListingPublishStep Step { get; }

    /// <summary>What GitHub said, or the repository a failure is about.</summary>
    public string? Detail { get; }

    /// <summary>When GitHub accepts requests again after a rate limit.</summary>
    public DateTimeOffset? RetryAt { get; }

    /// <summary>The GitHub id of the repository the failure is about, when known.</summary>
    public long? RepositoryId { get; }

    private static string Describe(ListingPublishFailure failure, ListingPublishStep? step, string? detail) =>
        $"{(step is null ? string.Empty : step + " failed: ")}{failure}{(detail is null ? string.Empty : ", " + detail)}";
}
