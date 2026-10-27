using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using Borea.Network.Listings;

namespace Borea.Network.GitHub;

/// <summary>
/// IPullRequestReviews and IPullRequestActions on the REST API, and the review decision of a merge through a GraphQL query. The pull request, its files,
/// its comments, its documents, the review decision and every action go with the token of the signed-in session, and the commit status goes without it,
/// because the App has no Commit statuses permission.
/// </summary>
public sealed partial class GitHubPullRequestReviews : IPullRequestReviews
{
    private readonly IGitHubSession _session;
    private readonly GitHubApi _api;
    private readonly IListingFormat _format;
    private readonly IListingOwnershipCheck _ownership;

    /// <param name="format">Reads the listing and pack documents.</param>
    public GitHubPullRequestReviews(IGitHubSession session, HttpClient http, IListingFormat format, IListingOwnershipCheck ownership, TimeProvider? time = null)
    {
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _api = new GitHubApi(session, http, time);
    }

    public async Task<PullRequestReview> ReadAsync(string repository, int number, CancellationToken cancellationToken = default)
    {
        var pulls = PullsUrlOf(repository, number);
        try
        {
            var pull = await _api.GetAsync<PullDto>(pulls, cancellationToken).ConfigureAwait(false);
            if (!Uri.TryCreate(pull.HtmlUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || pull.Number != number
                || pull.Head?.Sha is not { } head || !CommitSha().IsMatch(head) || pull.Base?.Ref is not { Length: > 0 } baseBranch)
            {
                throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
            }

            var files = FilesAsync(pulls, url, cancellationToken);
            var comments = CommentsAsync(repository, number, cancellationToken);
            var validate = ValidateAsync(repository, head, cancellationToken);
            await Task.WhenAll(files, comments, validate).ConfigureAwait(false);

            var author = pull.User is { Login: { } login, Id: > 0 } user ? new GitHubAccount(login, user.Id) : null;
            var documents = new List<PullRequestDocument>();
            foreach (var file in files.Result)
            {
                if (StewardQueueKinds.DocumentOf(repository, file.Path, file.Status) is { } kind)
                    documents.Add(await DocumentAsync(repository, file.Path, kind, baseBranch, head, author, cancellationToken).ConfigureAwait(false));
            }

            var claims = StewardQueueKinds.FirstClaims(files.Result.Select(file => new StewardQueueFile(file.Path, file.Status, file.PreviousPath)));
            var ownerRecords = new List<PullRequestOwnerRecord>();
            foreach (var file in files.Result)
            {
                if (StewardQueueKinds.IsOwnerRecord(repository, file.Path) || StewardQueueKinds.IsOwnerRecord(repository, file.PreviousPath))
                    ownerRecords.Add(await OwnerRecordAsync(repository, file, baseBranch, head, claims.Contains(file.Path), cancellationToken).ConfigureAwait(false));
            }

            var (status, validateFailure) = validate.Result;
            return new PullRequestReview(
                repository,
                number,
                url,
                pull.Title,
                pull.User?.Login,
                pull.Merged || pull.MergedAt is not null ? PullRequestState.Merged : pull.State == "closed" ? PullRequestState.Closed : PullRequestState.Open,
                pull.Draft,
                pull.Labels.Select(label => label.Name).ToList(),
                baseBranch,
                head,
                pull.Head.Repo?.FullName,
                status,
                validateFailure,
                IndexVerdict.Find(repository, comments.Result),
                files.Result,
                documents)
            {
                OwnerRecords = ownerRecords,
            };
        }
        catch (GitHubApiException exception)
        {
            throw exception.ToStewardException();
        }
        catch (ListingPublishException exception) when (exception.Failure == ListingPublishFailure.SignedOut)
        {
            throw new StewardException(StewardFailure.SignedOut, innerException: exception);
        }
    }

    private static string PullsUrlOf(string repository, int number)
    {
        if (!StewardQueue.Repositories.Contains(repository, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{repository} is no index repository.", nameof(repository));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return $"{GitHubApi.Root}/repos/{repository}/pulls/{number.ToString(CultureInfo.InvariantCulture)}";
    }

    private async Task<IReadOnlyList<PullRequestFile>> FilesAsync(string pulls, Uri url, CancellationToken cancellationToken)
    {
        var files = new List<PullRequestFile>();
        await foreach (var file in _api.GetPagesAsync<PullFileDto>(pulls + "/files", cancellationToken).ConfigureAwait(false))
            files.Add(new PullRequestFile(file.Filename, file.Status, file.PreviousFilename, file.Additions, file.Deletions, file.Patch, PullRequestFile.DiffUrlOf(url, file.Filename)));

        return files;
    }

    private async Task<IReadOnlyList<(string?, string?)>> CommentsAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var comments = new List<(string?, string?)>();
        var url = $"{GitHubApi.Root}/repos/{repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}/comments";
        await foreach (var comment in _api.GetPagesAsync<CommentDto>(url, cancellationToken).ConfigureAwait(false))
            comments.Add((comment.User?.Login, comment.Body));

        return comments;
    }

    /// <summary>The status validate of the commit, or why it could not be read, which leaves the rest of the review readable.</summary>
    private async Task<(ValidateStatus?, StewardException?)> ValidateAsync(string repository, string head, CancellationToken cancellationToken)
    {
        try
        {
            var combined = await _api.GetAsync<CombinedStatusDto>($"{GitHubApi.Root}/repos/{repository}/commits/{head}/status?per_page={GitHubApi.PageSize}", cancellationToken, anonymous: true).ConfigureAwait(false);
            if (combined.Statuses.FirstOrDefault(status => status.Context == PullRequestReview.StatusContext) is not { } validate)
                return (new ValidateStatus(ValidateState.Missing), null);

            var details = Uri.TryCreate(validate.TargetUrl, UriKind.Absolute, out var target) && target.Scheme == Uri.UriSchemeHttps ? target : null;
            return (new ValidateStatus(ValidateStatus.StateOf(validate.State), validate.Description, details), null);
        }
        catch (GitHubApiException exception)
        {
            return (null, exception.ToStewardException());
        }
    }

    private async Task<PullRequestDocument> DocumentAsync(string repository, string path, StewardQueueKind kind, string baseBranch, string head, GitHubAccount? author, CancellationToken cancellationToken)
    {
        // The head commit of a pull request from a fork is readable in the base repository too, as the checks read it.
        var file = await _api.ReadFileAsync(repository, path, head, cancellationToken).ConfigureAwait(false)
            ?? throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, $"{path} is not at {head}");
        if (file.Text is null)
            return new PullRequestDocument(path, kind, null, null, null, null);

        AuthoredTable document;
        try
        {
            document = _format.Read(file.Text);
        }
        catch (FormatException exception)
        {
            return new PullRequestDocument(path, kind, file.Text, null, exception.Message, null);
        }

        // The checks refuse a listing name without a lower-case .toml, and the proof reads no such listing, so it cannot be checked.
        var ownership = kind != StewardQueueKind.Listing ? null
            : author is null || !ListingOwnershipCheck.IsListingPath(path) ? ListingOwnership.Unknown
            : await _ownership.CheckPullRequestAsync(author, path, baseBranch, head, cancellationToken).ConfigureAwait(false);
        return new PullRequestDocument(path, kind, file.Text, document, null, ownership);
    }

    /// <summary>
    /// The owner record on the tip of the base branch, as the checks read the accepted owner, and at the head commit, with the login
    /// that GitHub gives the new account id now.
    /// </summary>
    private async Task<PullRequestOwnerRecord> OwnerRecordAsync(string repository, PullRequestFile file, string baseBranch, string head, bool isFirstClaim, CancellationToken cancellationToken)
    {
        var before = await OwnerTextAsync(repository, file.PreviousPath ?? file.Path, baseBranch, cancellationToken).ConfigureAwait(false);
        // a record that is removed, or renamed to another path, leaves the pack without a record
        var after = file.Status == "removed" || !StewardQueueKinds.IsOwnerRecord(repository, file.Path) ? null
            : await OwnerTextAsync(repository, file.Path, head, cancellationToken).ConfigureAwait(false)
                ?? throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, $"{file.Path} is not at {head}");
        var account = after?.Owner is { } owner ? await AccountAsync(owner, cancellationToken).ConfigureAwait(false) : null;
        return new PullRequestOwnerRecord(file.Path, before, after, account, isFirstClaim);
    }

    private async Task<PackOwnerText?> OwnerTextAsync(string repository, string path, string reference, CancellationToken cancellationToken)
    {
        if (!StewardQueueKinds.IsOwnerRecord(repository, path))
            return null;

        var file = await _api.ReadFileAsync(repository, path, reference, cancellationToken).ConfigureAwait(false);
        return file is null ? null : new PackOwnerText(file.Text, ListingPackOwner.Parse(file.Text));
    }

    /// <summary>GET /user/{id} names the login that the account id has now. A failed read only leaves the answer unknown.</summary>
    private async Task<PackOwnerAccount> AccountAsync(ListingPackOwner owner, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _api.SendAsync(HttpMethod.Get, $"{GitHubApi.Root}/user/{owner.Id.ToString(CultureInfo.InvariantCulture)}", null, cancellationToken).ConfigureAwait(false);
            if (reply.Status == HttpStatusCode.NotFound)
                return PackOwnerAccount.Of(owner, null);

            var user = GitHubApi.Parse<UserDto>(GitHubApi.Ensure(reply));
            return user.Id == owner.Id && user.Login is { Length: > 0 } login
                ? PackOwnerAccount.Of(owner, login)
                : new PackOwnerAccount(PackOwnerAccountState.Unknown);
        }
        catch (GitHubApiException exception) when (exception.Failure != GitHubApiFailure.SignedOut)
        {
            return new PackOwnerAccount(PackOwnerAccountState.Unknown);
        }
    }

    [GeneratedRegex(@"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z")]
    private static partial Regex CommitSha();

    private sealed class PullDto
    {
        public int Number { get; set; }

        public string Title { get; set; } = string.Empty;

        public string HtmlUrl { get; set; } = string.Empty;

        public string? State { get; set; }

        public bool Draft { get; set; }

        public bool Merged { get; set; }

        public DateTimeOffset? MergedAt { get; set; }

        public UserDto? User { get; set; }

        public List<LabelDto> Labels { get; set; } = [];

        public BranchDto? Head { get; set; }

        public BranchDto? Base { get; set; }
    }

    private sealed class BranchDto
    {
        public string? Ref { get; set; }

        public string? Sha { get; set; }

        public RepositoryDto? Repo { get; set; }
    }

    private sealed class RepositoryDto
    {
        public string? FullName { get; set; }
    }

    private sealed class PullFileDto
    {
        public string Filename { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? PreviousFilename { get; set; }

        public int Additions { get; set; }

        public int Deletions { get; set; }

        public string? Patch { get; set; }
    }

    private sealed class CommentDto
    {
        public string? Body { get; set; }

        public UserDto? User { get; set; }
    }

    private sealed class UserDto
    {
        public string? Login { get; set; }

        public long Id { get; set; }
    }

    private sealed class LabelDto
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class CombinedStatusDto
    {
        public List<StatusDto> Statuses { get; set; } = [];
    }

    private sealed class StatusDto
    {
        public string? Context { get; set; }

        public string? State { get; set; }

        public string? Description { get; set; }

        public string? TargetUrl { get; set; }
    }
}
