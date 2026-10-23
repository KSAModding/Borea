using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Stewardship;
using Borea.Network.GitHub;

namespace Borea.Network.Listings;

/// <summary>
/// IListingPublisher on the signed-in GitHub session: the author's fork of content-index where the Borea App is installed,
/// one branch per listing or pack version, and the pull request from the author's own account. The ownership pre-check of a listing
/// is <see cref="ListingOwnershipCheck"/>, and the one of a pack follows tools/pack_ownership.py of content-index.
/// </summary>
public sealed class ListingPublisher : IListingPublisher
{
    internal const string Api = GitHubApi.Root;

    internal const string Upstream = ListingPullRequestLinks.Repository;

    internal const string StatusContext = PullRequestReview.StatusContext;

    internal const string StewardLabel = StewardQueueKinds.NeedsStewardLabel;

    internal const string MergingDescription = "validated, arming auto-merge";

    private readonly IGitHubSession _session;
    private readonly GitHubApi _api;
    private readonly ListingOwnershipCheck _ownership;
    private readonly string _base;

    /// <param name="http">Reads SpaceDock and the GitHub repositories that the token cannot reach.</param>
    /// <param name="format">Reads the marker file.</param>
    /// <param name="baseBranch">The branch of content-index that the pull requests go to. Null takes <see cref="ListingPullRequestLinks.Branch"/>.</param>
    public ListingPublisher(IGitHubSession session, HttpClient http, IListingFormat format, TimeProvider? time = null, string? baseBranch = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _base = baseBranch ?? ListingPullRequestLinks.Branch;
        _api = new GitHubApi(session, http, time, _base);
        _ownership = new ListingOwnershipCheck(_api, http, format);
    }

    public async Task<ListingOwnership> CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, ContentIndexSnapshot? snapshot = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submitted);
        var login = Login(ListingPublishStep.Ownership);
        try
        {
            var user = await GetAsync<UserDto>($"{Api}/user", ListingPublishStep.Ownership, cancellationToken).ConfigureAwait(false);
            var ownership = submitted.IsPack
                ? await VerifyPackAsync(submitted.Id, new ListingPackOwner(user.Login.Length > 0 ? user.Login : login, user.Id), snapshot, cancellationToken).ConfigureAwait(false)
                : await _ownership.CheckAsync(new GitHubAccount(login, user.Id), submitted, listed, cancellationToken).ConfigureAwait(false);
            if (ownership.State is not (ListingOwnershipState.Verified or ListingOwnershipState.FirstClaim))
                return ownership;

            // The files go into this pull request, and content-index sends a pull request with other files to a steward.
            string[] paths = ownership.State == ListingOwnershipState.FirstClaim ? [submitted.Path, ListingPackOwner.PathOf(submitted.Id)] : [submitted.Path];
            var open = await FindOpenPullRequestAsync(login, paths, cancellationToken).ConfigureAwait(false);
            return open is { OnlyTheseFiles: false }
                ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PullRequestHasOtherFiles, PullRequest: open.Pull.Number)
                : ownership;
        }
        catch (ListingPublishException exception) when (exception.Failure != ListingPublishFailure.SignedOut)
        {
            return ListingOwnership.Unknown;
        }
    }

    public async Task<ListingPullRequest> PublishAsync(ListingSubmission submission, IProgress<ListingPublishStep>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var login = Login(ListingPublishStep.Fork);

        progress?.Report(ListingPublishStep.Fork);
        var fork = await FindForkAsync(login, cancellationToken).ConfigureAwait(false);
        submission = await WithOwnerRecordAsync(submission, cancellationToken).ConfigureAwait(false);

        progress?.Report(ListingPublishStep.FindPullRequest);
        if (await FindOpenPullRequestAsync(login, submission.Files.Select(file => file.Path).ToList(), cancellationToken).ConfigureAwait(false) is { } open)
        {
            if (!string.Equals(open.Pull.Head!.Repo!.FullName, fork.FullName, StringComparison.OrdinalIgnoreCase))
                throw new ListingPublishException(ListingPublishFailure.PullRequestNotOnFork, ListingPublishStep.FindPullRequest, open.Pull.Number.ToString(CultureInfo.InvariantCulture));

            progress?.Report(ListingPublishStep.Commit);
            return await CommitToOpenAsync(open.Pull, submission, cancellationToken).ConfigureAwait(false);
        }

        var (main, start) = await BranchStartAsync(fork, cancellationToken).ConfigureAwait(false);
        var listed = new List<GitHubFile?>();
        foreach (var file in submission.Files)
            listed.Add(await ReadFileAsync(Upstream, file.Path, main, ListingPublishStep.Branch, cancellationToken).ConfigureAwait(false));
        if (submission.Files.Select((file, index) => listed[index]?.Text == file.Text).All(same => same))
            throw new ListingPublishException(ListingPublishFailure.NoChange, ListingPublishStep.Commit);

        if (start != main)
            await EnsureForkInSyncAsync(fork, submission, start, listed, cancellationToken).ConfigureAwait(false);

        progress?.Report(ListingPublishStep.Branch);
        var branch = await CreateBranchAsync(fork, BranchName(submission), start, cancellationToken).ConfigureAwait(false);

        // The pull request opens after every file is on the branch, so the first run of the checks sees them all.
        progress?.Report(ListingPublishStep.Commit);
        for (var index = 0; index < submission.Files.Count; index++)
            await PutFileAsync(fork.FullName, branch, submission, submission.Files[index], listed[index]?.Sha, cancellationToken).ConfigureAwait(false);

        progress?.Report(ListingPublishStep.PullRequest);
        var (title, text) = PullRequestTextOf(submission);
        var body = new Dictionary<string, object>
        {
            ["title"] = title,
            ["head"] = $"{fork.Owner}:{branch}",
            ["base"] = _base,
            ["body"] = text,
            ["maintainer_can_modify"] = true,
        };
        var reply = await SendAsync(HttpMethod.Post, $"{Api}/repos/{Upstream}/pulls", body, ListingPublishStep.PullRequest, cancellationToken).ConfigureAwait(false);
        var pull = Parse<PullDto>(Ensure(reply, ListingPublishStep.PullRequest), ListingPublishStep.PullRequest);
        return new ListingPullRequest(pull.Number, PullRequestUrl(pull, ListingPublishStep.PullRequest), ListingPublishOutcome.Opened);
    }

    public async Task<ListingPullRequestStatus> GetStatusAsync(int number, CancellationToken cancellationToken = default)
    {
        const ListingPublishStep step = ListingPublishStep.Status;
        Login(step);
        var pull = await GetAsync<PullDto>($"{Api}/repos/{Upstream}/pulls/{number}", step, cancellationToken).ConfigureAwait(false);
        if (pull.Merged)
            return new ListingPullRequestStatus(ListingPullRequestState.Merged, null);
        if (pull.State == "closed")
            return new ListingPullRequestStatus(ListingPullRequestState.Closed, null);

        var headSha = pull.Head?.Sha ?? throw Unexpected(step);
        var combined = await GetPublicAsync<CombinedStatusDto>($"{Api}/repos/{Upstream}/commits/{headSha}/status", step, cancellationToken).ConfigureAwait(false);
        var comments = await GetPublicAsync<List<CommentDto>>($"{Api}/repos/{Upstream}/issues/{number}/comments?per_page=100", step, cancellationToken).ConfigureAwait(false);

        var verdict = IndexVerdict.Find(Upstream, comments.Select(comment => ((string?)comment.User?.Login, comment.Body)));

        var validate = combined.Statuses.FirstOrDefault(status => status.Context == StatusContext);
        var state = validate is null ? ListingPullRequestState.ChecksRunning : validate.State switch
        {
            "failure" => ListingPullRequestState.Rejected,
            "error" => ListingPullRequestState.CouldNotEvaluate,
            "success" when validate.Description == MergingDescription && pull.Labels.All(label => label.Name != StewardLabel) => ListingPullRequestState.ValidatedMerging,
            "success" => ListingPullRequestState.WaitingForSteward,
            _ => ListingPullRequestState.ChecksRunning,
        };
        return new ListingPullRequestStatus(state, verdict);
    }

    private string Login(ListingPublishStep step) =>
        _session.State is { Status: GitHubSessionStatus.SignedIn, Login: { } login }
            ? login
            : throw new ListingPublishException(ListingPublishFailure.SignedOut, step);

    /// <summary>
    /// A pack has no release host, so its owner record on main decides (RFC 0033). The paths of content-index are case-sensitive,
    /// so a missing record makes the id free only while no listing or pack of the snapshot holds it in another letter case.
    /// </summary>
    private async Task<ListingOwnership> VerifyPackAsync(string id, ListingPackOwner author, ContentIndexSnapshot? snapshot, CancellationToken cancellationToken)
    {
        // An id that cannot be a folder of content-index has no owner record, and it is never free to claim.
        if (!ModIds.IsValid(id))
            return ListingOwnership.Unknown;

        var record = await ReadFileAsync(Upstream, ListingPackOwner.PathOf(id), _base, ListingPublishStep.Ownership, cancellationToken).ConfigureAwait(false);
        if (record is not null)
        {
            return ListingPackOwner.Parse(record.Text) switch
            {
                null => ListingOwnership.Unknown,
                { Id: var owner } when owner == author.Id => new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.PackOwner),
                var other => new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PackOwnedByOther, PackOwner: other.Login),
            };
        }

        if (snapshot is null)
            return ListingOwnership.Unknown;

        return ListingPackOwner.HolderOf(snapshot, id) is { } holder
            ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PackIdTaken, TakenBy: holder)
            : new ListingOwnership(ListingOwnershipState.FirstClaim, Claim: author);
    }

    /// <summary>A pack without an owner record on main is a first claim, so the record of the signed-in account goes into the same pull request.</summary>
    private async Task<ListingSubmission> WithOwnerRecordAsync(ListingSubmission submission, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Ownership;
        if (!submission.IsPack)
            return submission;

        var path = ListingPackOwner.PathOf(submission.Id);
        if (await ReadFileAsync(Upstream, path, _base, step, cancellationToken).ConfigureAwait(false) is not null)
            return submission;

        var user = await GetAsync<UserDto>($"{Api}/user", step, cancellationToken).ConfigureAwait(false);
        if (user.Id <= 0 || user.Login.Length == 0)
            throw Unexpected(step);

        return submission with { Files = [.. submission.Files.Where(file => file.Path != path), new ListingFile(path, new ListingPackOwner(user.Login, user.Id).ToJson())] };
    }

    /// <summary>
    /// The author's open pull request that changes the document, the first of <paramref name="paths"/>. A pull request
    /// of the same pack with another version file is another pull request, even when it shares the owner record.
    /// </summary>
    private async Task<OpenPullRequest?> FindOpenPullRequestAsync(string login, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.FindPullRequest;
        var pulls = Pages(step, _api.GetPagesAsync<PullDto>($"{Api}/repos/{Upstream}/pulls?state=open", cancellationToken), cancellationToken);
        await foreach (var pull in pulls.ConfigureAwait(false))
        {
            if (!string.Equals(pull.User?.Login, login, StringComparison.OrdinalIgnoreCase) || pull.Head?.Repo is null)
                continue;

            var files = await FilesAsync(pull.Number, cancellationToken).ConfigureAwait(false);
            if (files.Contains(paths[0], StringComparer.Ordinal))
                return new OpenPullRequest(pull, files.All(file => paths.Contains(file, StringComparer.Ordinal)));
        }

        return null;
    }

    private async Task<List<string>> FilesAsync(int number, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        var files = Pages(ListingPublishStep.FindPullRequest, _api.GetPagesAsync<PullFileDto>($"{Api}/repos/{Upstream}/pulls/{number}/files", cancellationToken), cancellationToken);
        await foreach (var file in files.ConfigureAwait(false))
            names.Add(file.Filename);

        return names;
    }

    private async Task<ListingPullRequest> CommitToOpenAsync(PullDto pull, ListingSubmission submission, CancellationToken cancellationToken)
    {
        var url = PullRequestUrl(pull, ListingPublishStep.Commit);
        var repository = pull.Head!.Repo!.FullName;
        var branch = pull.Head.Ref;
        var outcome = ListingPublishOutcome.Unchanged;
        foreach (var file in submission.Files)
        {
            var current = await ReadFileAsync(repository, file.Path, branch, ListingPublishStep.Commit, cancellationToken).ConfigureAwait(false);
            if (current?.Text == file.Text)
                continue;

            await PutFileAsync(repository, branch, submission, file, current?.Sha, cancellationToken).ConfigureAwait(false);
            outcome = ListingPublishOutcome.Updated;
        }

        return new ListingPullRequest(pull.Number, url, outcome);
    }

    /// <summary>
    /// The fork of content-index among the repositories of the App's installation on the author's own account.
    /// Without one, a read without the token tells a missing fork from a fork without the App.
    /// </summary>
    private async Task<Fork> FindForkAsync(string login, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Fork;
        if (await FindInstalledForkAsync(login, cancellationToken).ConfigureAwait(false) is { } fork)
            return fork;

        var reply = await SendAsync(HttpMethod.Get, $"{Api}/repos/{login}/content-index", null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status != HttpStatusCode.NotFound && Parse<RepositoryDto>(Ensure(reply, step), step) is var named && IsForkOfUpstream(named))
            throw new ListingPublishException(ListingPublishFailure.AppNotOnFork, step, named.FullName, repositoryId: named.Id > 0 ? named.Id : null);

        throw new ListingPublishException(ListingPublishFailure.NoFork, step);
    }

    /// <summary>The fork of content-index among the repositories of the App's installation on the author's own account, or null.</summary>
    private async Task<Fork?> FindInstalledForkAsync(string login, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Fork;
        if (await FindInstallationAsync(login, cancellationToken).ConfigureAwait(false) is not { } installation)
            return null;

        var repositories = Pages(step, _api.GetPagesAsync<InstallationRepositoriesDto, RepositoryDto>($"{Api}/user/installations/{installation}/repositories", page => page.Repositories, cancellationToken), cancellationToken);
        await foreach (var candidate in repositories.ConfigureAwait(false))
        {
            if (!candidate.Fork)
                continue;

            var repository = await GetAsync<RepositoryDto>($"{Api}/repos/{candidate.FullName}", step, cancellationToken).ConfigureAwait(false);
            if (IsForkOfUpstream(repository) && ForkOf(repository) is { } fork)
                return fork;
        }

        return null;
    }

    private async Task<long?> FindInstallationAsync(string login, CancellationToken cancellationToken)
    {
        var installations = Pages(ListingPublishStep.Fork, _api.GetPagesAsync<InstallationsDto, InstallationDto>($"{Api}/user/installations", page => page.Installations, cancellationToken), cancellationToken);
        await foreach (var installation in installations.ConfigureAwait(false))
        {
            if (string.Equals(installation.Account?.Login, login, StringComparison.OrdinalIgnoreCase))
                return installation.Id;
        }

        return null;
    }

    private static bool IsForkOfUpstream(RepositoryDto repository) =>
        repository.Fork && string.Equals(repository.Parent?.FullName, Upstream, StringComparison.OrdinalIgnoreCase);

    private static Fork? ForkOf(RepositoryDto repository)
    {
        var slash = repository.FullName.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && repository.DefaultBranch.Length > 0 ? new Fork(repository.FullName, repository.FullName[..slash], repository.DefaultBranch) : null;
    }

    private static string BranchName(ListingSubmission submission) =>
        (submission.PackVersion is { } version ? $"pack-{submission.Id}-{version}" : "listing-" + submission.Id).ToLowerInvariant();

    /// <summary>Creates <paramref name="baseName"/> at <paramref name="sha"/>, or the first free name with -N. A refusal asks for a sync of the fork.</summary>
    private async Task<string> CreateBranchAsync(Fork fork, string baseName, string sha, CancellationToken cancellationToken)
    {
        try
        {
            return await At(ListingPublishStep.Branch, _api.CreateFreeBranchAsync(fork.FullName, baseName, sha, cancellationToken)).ConfigureAwait(false);
        }
        catch (ListingPublishException exception) when (exception.Failure == ListingPublishFailure.Forbidden)
        {
            throw new ListingPublishException(ListingPublishFailure.ForkNeedsSync, ListingPublishStep.Branch, fork.FullName, innerException: exception);
        }
    }

    /// <summary>
    /// The head of the base branch, and the commit that the branch starts at. The branch starts at a commit the fork already has, because writing newer history
    /// of content-index into the fork needs the Workflows permission whenever that history changes a workflow.
    /// </summary>
    private async Task<(string Main, string Start)> BranchStartAsync(Fork fork, CancellationToken cancellationToken)
    {
        var compare = await GetAsync<CompareDto>(
            $"{Api}/repos/{Upstream}/compare/{_base}...{fork.FullName.Replace('/', ':')}:{Uri.EscapeDataString(fork.DefaultBranch)}?per_page=1",
            ListingPublishStep.Branch,
            cancellationToken).ConfigureAwait(false);
        var main = compare.BaseCommit?.Sha ?? throw Unexpected(ListingPublishStep.Branch);
        var start = compare.MergeBaseCommit?.Sha ?? throw Unexpected(ListingPublishStep.Branch);
        return (main, start);
    }

    /// <summary>Refuses with ForkNeedsSync when a document of the submission differs between <paramref name="start"/> and the base branch, because the branch would start from the older document.</summary>
    private async Task EnsureForkInSyncAsync(Fork fork, ListingSubmission submission, string start, IReadOnlyList<GitHubFile?> listed, CancellationToken cancellationToken)
    {
        for (var index = 0; index < submission.Files.Count; index++)
        {
            var based = await ReadFileAsync(Upstream, submission.Files[index].Path, start, ListingPublishStep.Branch, cancellationToken).ConfigureAwait(false);
            if (based?.Sha != listed[index]?.Sha)
                throw new ListingPublishException(ListingPublishFailure.ForkNeedsSync, ListingPublishStep.Branch, fork.FullName);
        }
    }

    private async Task PutFileAsync(string repository, string branch, ListingSubmission submission, ListingFile file, string? sha, CancellationToken cancellationToken)
    {
        var message = submission switch
        {
            _ when file.Path != submission.Document.Path => $"Claim {submission.Id}",
            { PackVersion: { } version } => $"Add {submission.Id} {version}",
            { IsEdit: true } => $"Update {submission.Id}",
            _ => $"List {submission.Id}",
        };
        var body = new Dictionary<string, object>
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Text)),
            ["branch"] = branch,
        };
        if (sha is not null)
            body["sha"] = sha;

        Ensure(await SendAsync(HttpMethod.Put, $"{Api}/repos/{repository}/contents/{file.Path}", body, ListingPublishStep.Commit, cancellationToken).ConfigureAwait(false), ListingPublishStep.Commit);
    }

    private static (string Title, string Text) PullRequestTextOf(ListingSubmission submission)
    {
        var name = submission.Name.Trim().Length > 0 ? submission.Name.Trim() : submission.Id;
        return submission switch
        {
            { PackVersion: { } version, ClaimsPack: true } => ($"List {name}", $"Lists the pack {name} with its first version {version}."),
            { PackVersion: { } version } => ($"Add {name} {version}", $"Adds version {version} of the pack {name}."),
            { IsEdit: true } => ($"Update {name}", $"Updates the listing of {name}."),
            _ => ($"List {name}", $"Lists {name}."),
        };
    }

    private Task<GitHubFile?> ReadFileAsync(string repository, string path, string reference, ListingPublishStep step, CancellationToken cancellationToken) =>
        At(step, _api.ReadFileAsync(repository, path, reference, cancellationToken));

    private Task<T> GetAsync<T>(string url, ListingPublishStep step, CancellationToken cancellationToken) =>
        At(step, _api.GetAsync<T>(url, cancellationToken));

    private Task<T> GetPublicAsync<T>(string url, ListingPublishStep step, CancellationToken cancellationToken) =>
        At(step, _api.GetPublicAsync<T>(url, cancellationToken));

    private Task<GitHubReply> SendAsync(HttpMethod method, string url, object? body, ListingPublishStep step, CancellationToken cancellationToken, bool anonymous = false) =>
        At(step, _api.SendAsync(method, url, body, cancellationToken, anonymous));

    private static string Ensure(GitHubReply reply, ListingPublishStep step) => At(step, () => GitHubApi.Ensure(reply));

    private static T Parse<T>(string body, ListingPublishStep step) => At(step, () => GitHubApi.Parse<T>(body));

    private static async Task<T> At<T>(ListingPublishStep step, Task<T> call)
    {
        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (GitHubApiException exception)
        {
            throw Failed(exception, step);
        }
    }

    private static T At<T>(ListingPublishStep step, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (GitHubApiException exception)
        {
            throw Failed(exception, step);
        }
    }

    private static async IAsyncEnumerable<T> Pages<T>(ListingPublishStep step, IAsyncEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = items.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (await At(step, enumerator.MoveNextAsync().AsTask()).ConfigureAwait(false))
                yield return enumerator.Current;
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ListingPublishException Failed(GitHubApiException exception, ListingPublishStep step)
    {
        var failure = exception.Failure switch
        {
            GitHubApiFailure.SignedOut => ListingPublishFailure.SignedOut,
            GitHubApiFailure.RateLimited => ListingPublishFailure.RateLimited,
            GitHubApiFailure.NotFound => ListingPublishFailure.NotFound,
            GitHubApiFailure.Refused or GitHubApiFailure.ProtectedBranch => ListingPublishFailure.Refused,
            GitHubApiFailure.Forbidden => ListingPublishFailure.Forbidden,
            GitHubApiFailure.NetworkError => ListingPublishFailure.NetworkError,
            _ => ListingPublishFailure.UnexpectedResponse,
        };
        return new ListingPublishException(failure, step, exception.Detail, exception.RetryAt, exception.InnerException);
    }

    private static Uri PullRequestUrl(PullDto pull, ListingPublishStep step) =>
        Uri.TryCreate(pull.HtmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && pull.Number > 0 ? url : throw Unexpected(step);

    private static ListingPublishException Unexpected(ListingPublishStep step) => new(ListingPublishFailure.UnexpectedResponse, step);

    private sealed record Fork(string FullName, string Owner, string DefaultBranch);

    private sealed record OpenPullRequest(PullDto Pull, bool OnlyTheseFiles);

    private sealed class UserDto
    {
        public long Id { get; set; }

        public string Login { get; set; } = string.Empty;
    }

    private sealed class OwnerDto
    {
        public long Id { get; set; }

        public string Login { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class RepositoryDto
    {
        public long Id { get; set; }

        public string FullName { get; set; } = string.Empty;

        public bool Fork { get; set; }

        public string DefaultBranch { get; set; } = string.Empty;

        public OwnerDto? Owner { get; set; }

        public RepositoryDto? Parent { get; set; }
    }

    private sealed class InstallationsDto
    {
        public List<InstallationDto> Installations { get; set; } = [];
    }

    private sealed class InstallationDto
    {
        public long Id { get; set; }

        public OwnerDto? Account { get; set; }
    }

    private sealed class InstallationRepositoriesDto
    {
        public List<RepositoryDto> Repositories { get; set; } = [];
    }

    private sealed class CompareDto
    {
        public CommitDto? BaseCommit { get; set; }

        public CommitDto? MergeBaseCommit { get; set; }
    }

    private sealed class CommitDto
    {
        public string? Sha { get; set; }
    }

    private sealed class PullDto
    {
        public int Number { get; set; }

        public string HtmlUrl { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public bool Merged { get; set; }

        public OwnerDto? User { get; set; }

        public PullHeadDto? Head { get; set; }

        public List<LabelDto> Labels { get; set; } = [];
    }

    private sealed class PullHeadDto
    {
        public string Ref { get; set; } = string.Empty;

        public string? Sha { get; set; }

        public RepositoryDto? Repo { get; set; }
    }

    private sealed class LabelDto
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PullFileDto
    {
        public string Filename { get; set; } = string.Empty;
    }

    private sealed class CombinedStatusDto
    {
        public List<CommitStatusDto> Statuses { get; set; } = [];
    }

    private sealed class CommitStatusDto
    {
        public string Context { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string? Description { get; set; }
    }

    private sealed class CommentDto
    {
        public string? Body { get; set; }

        public OwnerDto? User { get; set; }
    }
}
