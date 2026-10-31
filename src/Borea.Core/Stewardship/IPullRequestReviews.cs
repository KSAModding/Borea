using System.Security.Cryptography;
using System.Text;
using Borea.Core.Listings;

namespace Borea.Core.Stewardship;

/// <summary>One pull request of an index repository as a steward reviews it. It reads only.</summary>
public interface IPullRequestReviews
{
    /// <summary>
    /// Reads the pull request, its changed files and its verdict with the token of the signed-in account, and each listing or pack document
    /// at its head commit. The status validate of that commit is read without the token, because the App has no Commit statuses permission.
    /// A listing, vehicle or save document also gets the ownership proof of the pull request author against the listing on the base branch (RFC 0048),
    /// and a vehicle or save also gets its newest release on main of content-index-releases.
    /// </summary>
    /// <param name="repository">An index repository as owner/name.</param>
    /// <exception cref="ArgumentException">The repository is no index repository.</exception>
    /// <exception cref="StewardException">The pull request could not be read, or the session is signed out.</exception>
    Task<PullRequestReview> ReadAsync(string repository, int number, CancellationToken cancellationToken = default);
}

/// <param name="Repository">The repository as owner/name.</param>
/// <param name="Author">The login that opened the pull request, or null when the account is gone.</param>
/// <param name="Labels">The label names of the pull request.</param>
/// <param name="BaseBranch">The branch that the pull request merges into.</param>
/// <param name="HeadCommit">The commit that the files, the documents and <paramref name="Validate"/> belong to.</param>
/// <param name="HeadRepository">The repository of the head branch as owner/name, or null when it is gone.</param>
/// <param name="Validate">The status validate on the head commit, or null when it could not be read.</param>
/// <param name="ValidateFailure">Why the status could not be read, or null.</param>
/// <param name="Verdict">The verdict comment of the checks without its marker, or null.</param>
/// <param name="Documents">The listing, pack, vehicle and save documents that the pull request adds or changes, in the order of <paramref name="Files"/>.</param>
public sealed record PullRequestReview(
    string Repository,
    int Number,
    Uri Url,
    string Title,
    string? Author,
    PullRequestState State,
    bool IsDraft,
    IReadOnlyList<string> Labels,
    string BaseBranch,
    string HeadCommit,
    string? HeadRepository,
    ValidateStatus? Validate,
    StewardException? ValidateFailure,
    string? Verdict,
    IReadOnlyList<PullRequestFile> Files,
    IReadOnlyList<PullRequestDocument> Documents)
{
    /// <summary>The context of the commit status that the checks of each index repository post.</summary>
    public const string StatusContext = "validate";

    /// <summary>
    /// The checks workflow of content-index, where a steward runs the checks of a pull request again, for example after its author added
    /// the topic without a push. Borea asks for no Actions permission, so it does not start the run itself.
    /// </summary>
    public static Uri ChecksWorkflowUrl { get; } = new($"https://github.com/{ListingPullRequestLinks.Repository}/actions/workflows/checks.yml");

    /// <summary>Only the checks of content-index run again for one pull request.</summary>
    public bool CanRunChecks => string.Equals(Repository, ListingPullRequestLinks.Repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>The head branch is in another repository, or its repository is gone.</summary>
    public bool IsFromFork => !string.Equals(HeadRepository, Repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>The pack owner records that the pull request adds, changes or removes, in the order of <see cref="Files"/>.</summary>
    public IReadOnlyList<PullRequestOwnerRecord> OwnerRecords { get; init; } = [];
}

/// <summary>
/// A change of packs/&lt;id&gt;/owner.json in content-index. Only the first claim of a pack merges itself, so a steward reads who
/// owns the pack before and after any other change, such as a handover (RFC 0080).
/// </summary>
/// <param name="Path">The path of the record after the change.</param>
/// <param name="Before">The record on the base branch, at the old path of a renamed one, or null when the base branch has none.</param>
/// <param name="After">The record at the head commit, or null when the pull request removes it.</param>
/// <param name="Account">What GitHub says about the account id of <paramref name="After"/>, or null when it names no owner.</param>
/// <param name="IsFirstClaim">The pull request adds the record together with a version of its pack, which merges itself.</param>
public sealed record PullRequestOwnerRecord(string Path, PackOwnerText? Before, PackOwnerText? After, PackOwnerAccount? Account, bool IsFirstClaim);

/// <param name="Text">The file as UTF-8 text, or null when it is not.</param>
/// <param name="Owner">The login and the account id it names, or null when it is no owner record.</param>
public sealed record PackOwnerText(string? Text, ListingPackOwner? Owner);

/// <summary>
/// Whether the account id of an owner record still has the login the record names. A login can be renamed and then taken by
/// another account, so the checks compare the account id, and a login that changed tells the steward to look closer.
/// </summary>
/// <param name="Login">The login that GitHub gives the account id now, or null when there is no such account or GitHub did not answer.</param>
public sealed record PackOwnerAccount(PackOwnerAccountState State, string? Login = null)
{
    /// <summary>Compares the answer of GET /user/{id} with the record. Logins compare without case, as GitHub compares them.</summary>
    /// <param name="current">The login that GitHub gives the account id, or null when GitHub has no such account.</param>
    public static PackOwnerAccount Of(ListingPackOwner owner, string? current)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return current is null ? new PackOwnerAccount(PackOwnerAccountState.Gone)
            : string.Equals(current, owner.Login, StringComparison.OrdinalIgnoreCase) ? new PackOwnerAccount(PackOwnerAccountState.Same, current)
            : new PackOwnerAccount(PackOwnerAccountState.OtherLogin, current);
    }
}

public enum PackOwnerAccountState
{
    /// <summary>The account id still has the login of the record.</summary>
    Same,

    /// <summary>The account id has another login now.</summary>
    OtherLogin,

    /// <summary>GitHub has no account with the id.</summary>
    Gone,

    /// <summary>GitHub could not be asked.</summary>
    Unknown,
}

public enum PullRequestState
{
    Open,
    Closed,
    Merged,
}

public enum ValidateState
{
    Unknown,

    /// <summary>The head commit has no status validate yet.</summary>
    Missing,

    Pending,
    Success,
    Failure,
    Error,
}

/// <param name="Description">What the checks say with the state.</param>
/// <param name="Details">The run that posted the state, or null.</param>
public sealed record ValidateStatus(ValidateState State, string? Description = null, Uri? Details = null)
{
    /// <summary>The state of a commit status as GitHub names it.</summary>
    public static ValidateState StateOf(string? state) => state switch
    {
        "success" => ValidateState.Success,
        "failure" => ValidateState.Failure,
        "error" => ValidateState.Error,
        "pending" => ValidateState.Pending,
        _ => ValidateState.Unknown,
    };
}

/// <param name="Path">The path of the file after the change.</param>
/// <param name="Status">The status that GitHub gives the file, such as added, modified, removed or renamed.</param>
/// <param name="PreviousPath">The old path of a renamed file, or null.</param>
/// <param name="Patch">The unified diff of the file, or null when GitHub sends none, as for a binary or a very large file.</param>
/// <param name="DiffUrl">The file in the Files view of the pull request on GitHub.</param>
public sealed record PullRequestFile(string Path, string Status, string? PreviousPath, int Additions, int Deletions, string? Patch, Uri DiffUrl)
{
    /// <summary>GitHub names the diff of a file on the Files view by the SHA-256 of its path.</summary>
    public static Uri DiffUrlOf(Uri pullRequest, string path)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        ArgumentNullException.ThrowIfNull(path);
        var anchor = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        return new Uri($"{pullRequest.AbsoluteUri.TrimEnd('/')}/files#diff-{anchor}");
    }
}

/// <param name="Kind">Listing, Pack, Vehicle or Save.</param>
/// <param name="Text">The file at the head commit, or null when it is no UTF-8 text.</param>
/// <param name="Document">The parsed document, or null when it does not parse.</param>
/// <param name="ParseError">Why <paramref name="Text"/> does not parse, or null.</param>
/// <param name="Ownership">For a listing, vehicle or save that parses, the ownership proof of the pull request author, else null.</param>
/// <param name="NewestRelease">For a vehicle or save that parses, its newest release on main of content-index-releases, or null when none is stamped or it could not be read.</param>
/// <param name="NewestReleaseFailure">Why the newest release could not be read, or null.</param>
public sealed record PullRequestDocument(string Path, StewardQueueKind Kind, string? Text, AuthoredTable? Document, string? ParseError, ListingOwnership? Ownership,
    PullRequestRelease? NewestRelease = null, StewardException? NewestReleaseFailure = null);

/// <summary>A stamped release of a vehicle or save.</summary>
/// <param name="Folder">install.folder, the game folder that the release installs as, or null when the file names none.</param>
public sealed record PullRequestRelease(string Version, string? Folder);
