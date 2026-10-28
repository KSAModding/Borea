namespace Borea.Core.Stewardship;

/// <summary>
/// The steward edits of index-status.toml in content-index. Each change goes to a steward branch cut from the base branch,
/// and its pull request waits for a steward, because the file is code-owned.
/// </summary>
public interface IIndexStatusEditor
{
    /// <summary>The entries of the file on the base branch, and the open pull requests that change it.</summary>
    /// <exception cref="StewardException">GitHub could not be read, or the file has a form that Borea does not edit.</exception>
    Task<IndexStatusOverview> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the change against the base branch as the checks of content-index would, and names what the steward should know first.
    /// It leaves out the reason, which the steward types after the check, and writes nothing.
    /// </summary>
    /// <exception cref="StewardException">GitHub could not be read, the account is no steward of content-index, or the file has a form that Borea does not edit.</exception>
    Task<IndexStatusCheck> CheckAsync(IndexStatusChange change, CancellationToken cancellationToken = default);

    /// <summary>Checks the change again, commits it to its steward branch and opens the pull request.</summary>
    /// <exception cref="IndexStatusRefusedException">The checks of content-index would refuse the change. Nothing was written.</exception>
    /// <exception cref="StewardException">A request failed.</exception>
    Task<IndexStatusPullRequest> OpenAsync(IndexStatusChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of <paramref name="ids"/> that the signed-in account owns, read at one commit of the base branch, so a steward sees
    /// that they are a party to a report before they answer it. An id that is in no document, or whose owner cannot be read, is left out.
    /// </summary>
    /// <exception cref="StewardException">The account is signed out, or GitHub could not be read.</exception>
    Task<IReadOnlyList<string>> OwnedAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

    /// <summary>The ids of the mods that the versions of the pack pin on the base branch, each once. A pack that is not there pins none.</summary>
    /// <exception cref="StewardException">GitHub could not be read, or a version of the pack does not parse.</exception>
    Task<IReadOnlyList<string>> PackMembersAsync(string packId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a request to take a mod out of a pack against the base branch: the versions that pin the mod, and the ownership proof of the
    /// reporter for the listing of the mod (RFC 0080). It writes nothing.
    /// </summary>
    /// <exception cref="StewardException">GitHub could not be read, the account is no steward of content-index, or a file does not parse.</exception>
    Task<PackLeaveCheck> CheckLeaveAsync(PackLeave leave, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the request again and opens one pull request that retracts every version of the pack that pins the mod. The reporter must
    /// have the ownership proof of the listing, or the steward must have confirmed it by the forums account.
    /// </summary>
    /// <exception cref="IndexStatusRefusedException">No version can be retracted, the ownership is not confirmed, or the checks of content-index would refuse the file. Nothing was written.</exception>
    /// <exception cref="StewardException">A request failed.</exception>
    Task<IndexStatusPullRequest> OpenLeaveAsync(PackLeave leave, CancellationToken cancellationToken = default);
}

public sealed record IndexStatusOverview(IReadOnlyList<IndexStatusEntry> Entries, IReadOnlyList<IndexStatusPullRequest> OpenPullRequests);

/// <param name="Conflicts">Whether GitHub cannot merge it into the base branch, or null while GitHub has not worked it out.</param>
public sealed record IndexStatusPullRequest(int Number, Uri Url, string Title, string? Author, bool? Conflicts = null);

/// <param name="Refusal">Why the checks of content-index would refuse the change, or null.</param>
/// <param name="OpenPullRequests">Open pull requests that change the file too, so the later one to merge conflicts. Null when they could not be read.</param>
/// <param name="Owners">The logins that own the listing or pack, as the ownership proof names them, without the signed-in steward.</param>
/// <param name="IsOwner">The signed-in steward owns the listing or pack, and POLICY.md asks a party to leave the case to another steward.</param>
public sealed record IndexStatusCheck(IndexStatusRefusal? Refusal, IReadOnlyList<IndexStatusPullRequest>? OpenPullRequests, IReadOnlyList<string> Owners, bool IsOwner);

public enum StewardFailure
{
    SignedOut,
    NotSteward,
    RateLimited,
    NotFound,
    Refused,
    Forbidden,
    NetworkError,

    /// <summary>A file on the base branch has a form that Borea does not edit.</summary>
    UnreadableFile,

    UnexpectedResponse,
}

/// <summary>A steward action failed. The message never holds the token.</summary>
public sealed class StewardException(StewardFailure failure, string? detail = null, DateTimeOffset? retryAt = null, Exception? innerException = null)
    : Exception(failure + (detail is null ? string.Empty : ", " + detail), innerException)
{
    public StewardFailure Failure { get; } = failure;

    /// <summary>What GitHub said, or what Borea could not read.</summary>
    public string? Detail { get; } = detail;

    /// <summary>When GitHub accepts requests again after a rate limit.</summary>
    public DateTimeOffset? RetryAt { get; } = retryAt;
}
