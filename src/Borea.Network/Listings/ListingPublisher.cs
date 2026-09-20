using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Network.GitHub;
using Borea.Network.SpaceDock;

namespace Borea.Network.Listings;

/// <summary>
/// IListingPublisher on the signed-in GitHub session: the author's fork of content-index where the Borea App is installed,
/// one branch per listing, and the pull request from the author's own account. The ownership pre-check follows tools/ownership.py of content-index.
/// </summary>
public sealed class ListingPublisher : IListingPublisher
{
    internal const string Api = "https://api.github.com";

    internal const string Upstream = ListingPullRequestLinks.Repository;

    internal const string VerdictMarker = "<!-- content-index:verdict -->";

    internal const string StatusContext = "validate";

    internal const string StewardLabel = "needs-steward";

    internal const string MergingDescription = "validated, arming auto-merge";

    internal static readonly TimeSpan SecondaryLimitWait = TimeSpan.FromMinutes(1);

    private const int SpaceDockGameId = 22409;

    private const int MaxRedirects = 5;

    private const int MaxBranchNumber = 100;

    private const int MaxPages = 10;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IGitHubSession _session;
    private readonly HttpClient _http;
    private readonly IListingFormat _format;
    private readonly TimeProvider _time;

    /// <param name="http">Reads SpaceDock and the GitHub repositories that the token cannot reach.</param>
    /// <param name="format">Reads the marker file.</param>
    public ListingPublisher(IGitHubSession session, HttpClient http, IListingFormat format, TimeProvider? time = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _time = time ?? TimeProvider.System;
    }

    public async Task<ListingOwnership> CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submitted);
        var login = Login(ListingPublishStep.Ownership);
        try
        {
            var user = await GetAsync<UserDto>($"{Api}/user", ListingPublishStep.Ownership, cancellationToken).ConfigureAwait(false);
            var ownership = await VerifyChangeAsync(submitted, listed, login, user.Id, cancellationToken).ConfigureAwait(false);
            if (ownership.State != ListingOwnershipState.Verified)
                return ownership;

            // The file goes into this pull request, and content-index sends a pull request with other files to a steward.
            var open = await FindOpenPullRequestAsync(login, submitted.Path, cancellationToken).ConfigureAwait(false);
            return open is { OnlyThisFile: false }
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

        progress?.Report(ListingPublishStep.FindPullRequest);
        if (await FindOpenPullRequestAsync(login, submission.Path, cancellationToken).ConfigureAwait(false) is { } open)
        {
            if (!string.Equals(open.Pull.Head!.Repo!.FullName, fork.FullName, StringComparison.OrdinalIgnoreCase))
                throw new ListingPublishException(ListingPublishFailure.PullRequestNotOnFork, ListingPublishStep.FindPullRequest, open.Pull.Number.ToString(CultureInfo.InvariantCulture));

            progress?.Report(ListingPublishStep.Commit);
            return await CommitToOpenAsync(open.Pull, submission, cancellationToken).ConfigureAwait(false);
        }

        var main = await GetAsync<RefDto>($"{Api}/repos/{Upstream}/git/ref/heads/{ListingPullRequestLinks.Branch}", ListingPublishStep.Branch, cancellationToken).ConfigureAwait(false);
        var sha = main.Object?.Sha ?? throw Unexpected(ListingPublishStep.Branch);
        var listed = await ReadFileAsync(Upstream, submission.Path, sha, ListingPublishStep.Branch, cancellationToken).ConfigureAwait(false);
        if (listed?.Text == submission.Text)
            throw new ListingPublishException(ListingPublishFailure.NoChange, ListingPublishStep.Commit);

        progress?.Report(ListingPublishStep.Branch);
        var branch = await CreateBranchAsync(fork, submission.Id, sha, cancellationToken).ConfigureAwait(false);

        progress?.Report(ListingPublishStep.Commit);
        await PutFileAsync(fork.FullName, branch, submission, listed?.Sha, cancellationToken).ConfigureAwait(false);

        progress?.Report(ListingPublishStep.PullRequest);
        var name = submission.Name.Trim().Length > 0 ? submission.Name.Trim() : submission.Id;
        var body = new Dictionary<string, object>
        {
            ["title"] = submission.IsEdit ? $"Update {name}" : $"List {name}",
            ["head"] = $"{fork.Owner}:{branch}",
            ["base"] = ListingPullRequestLinks.Branch,
            ["body"] = submission.IsEdit ? $"Updates the listing of {name}." : $"Lists {name}.",
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

        // Only the bot's own comment counts, because anybody can write the marker into a comment.
        var verdict = comments
            .FirstOrDefault(comment => comment.User?.Type == "Bot" && comment.Body?.Contains(VerdictMarker, StringComparison.Ordinal) == true)
            ?.Body!.Replace(VerdictMarker, string.Empty, StringComparison.Ordinal).Trim();

        var validate = combined.Statuses.FirstOrDefault(status => status.Context == StatusContext);
        var state = validate is null ? ListingPullRequestState.ChecksRunning : validate.State switch
        {
            "failure" => ListingPullRequestState.Rejected,
            "error" => ListingPullRequestState.CouldNotEvaluate,
            "success" when validate.Description == MergingDescription && pull.Labels.All(label => label.Name != StewardLabel) => ListingPullRequestState.ValidatedMerging,
            "success" => ListingPullRequestState.WaitingForSteward,
            _ => ListingPullRequestState.ChecksRunning,
        };
        return new ListingPullRequestStatus(state, string.IsNullOrEmpty(verdict) ? null : verdict);
    }

    private string Login(ListingPublishStep step) =>
        _session.State is { Status: GitHubSessionStatus.SignedIn, Login: { } login }
            ? login
            : throw new ListingPublishException(ListingPublishFailure.SignedOut, step);

    /// <summary>RFC 0048: an edit proves control of the listed host, and of the new one when it moves.</summary>
    private async Task<ListingOwnership> VerifyChangeAsync(ListingDraft submitted, ListingDraft? listed, string login, long authorId, CancellationToken cancellationToken)
    {
        if (listed is null)
            return await VerifyAsync(submitted, login, authorId, cancellationToken).ConfigureAwait(false);

        var current = await VerifyAsync(listed, login, authorId, cancellationToken).ConfigureAwait(false);
        var listedHost = ListingAuthority.Of(listed);
        var submittedHost = ListingAuthority.Of(submitted);
        if (listedHost is null ? submittedHost is null : listedHost.IsSameHost(submittedHost))
            return current;

        if (current.State != ListingOwnershipState.Verified
            && !await IsRenamedIntoAsync(listedHost, submittedHost, cancellationToken).ConfigureAwait(false))
        {
            return current;
        }

        return await VerifyAsync(submitted, login, authorId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ListingOwnership> VerifyAsync(ListingDraft draft, string login, long authorId, CancellationToken cancellationToken)
    {
        return ListingAuthority.Of(draft) switch
        {
            { Kind: ListingAuthority.GitHub } github => await VerifyRepositoryAsync(github.Target, draft.Id, login, authorId, cancellationToken).ConfigureAwait(false),
            { Kind: ListingAuthority.SpaceDock } spaceDock => await VerifySpaceDockAsync(spaceDock.Target, draft.Id, login, authorId, cancellationToken).ConfigureAwait(false),
            _ => new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoHost),
        };
    }

    private async Task<ListingOwnership> VerifyRepositoryAsync(string target, string id, string login, long authorId, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Ownership;
        var reply = await SendAsync(HttpMethod.Get, $"{Api}/repos/{target}", null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return NotVerified(ListingOwnershipProblem.RepositoryMissing, target);

        var repository = Parse<RepositoryDto>(Ensure(reply, step), step);
        if (!string.Equals(repository.FullName, target, StringComparison.OrdinalIgnoreCase))
            return NotVerified(ListingOwnershipProblem.RepositoryRenamed, target) with { RenamedTo = repository.FullName };
        if (repository.Fork)
            return NotVerified(ListingOwnershipProblem.RepositoryFork, target);
        if (repository.Owner?.Id == authorId)
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: target);

        var topics = await SendAsync(HttpMethod.Get, $"{Api}/repos/{target}/topics", null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        var names = topics.Status == HttpStatusCode.NotFound ? [] : Parse<TopicsDto>(Ensure(topics, step), step).Names;
        if (names.Contains(ListingOwnership.TopicFor(login), StringComparer.Ordinal))
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: target);

        var marker = await ReadFileAsync(target, ListingOwnership.MarkerPath, null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (marker?.Text is { } text && MarkerNames(text, id, login))
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.MarkerFile, Repository: target);

        return NotVerified(ListingOwnershipProblem.NoProof, target);
    }

    /// <summary>A SpaceDock mod binds to the GitHub repository of its source code link, which only its authors can set.</summary>
    private async Task<ListingOwnership> VerifySpaceDockAsync(string modId, string id, string login, long authorId, CancellationToken cancellationToken)
    {
        var unusable = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.SpaceDockModUnusable, SpaceDockMod: modId);
        if (modId.Length == 0 || !modId.All(char.IsAsciiDigit))
            return unusable;

        SpaceDockModDto? mod;
        try
        {
            using var response = await _http.GetAsync($"{SpaceDockModRepository.BaseUrl}/api/mod/{modId}", cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return unusable;

            var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            if (!response.IsSuccessStatusCode && !refused)
                return ListingOwnership.Unknown;

            mod = JsonSerializer.Deserialize<SpaceDockModDto>(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), Json);
            if (mod is null || (refused && !mod.Error))
                return ListingOwnership.Unknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return ListingOwnership.Unknown;
        }

        if (mod.Error)
            return unusable;
        if (mod.Id?.ToString() != modId)
            return ListingOwnership.Unknown;
        if (mod.GameId != SpaceDockGameId)
            return unusable;
        if (ListingAuthority.GitHubRepositoryOf(mod.SourceCode) is not { } repository)
            return unusable with { Problem = ListingOwnershipProblem.SpaceDockNoSourceLink };

        var result = await VerifyRepositoryAsync(repository, id, login, authorId, cancellationToken).ConfigureAwait(false);
        return result with { SpaceDockMod = modId };
    }

    /// <summary>GitHub answers the old name of a renamed or transferred repository with the new one.</summary>
    private async Task<bool> IsRenamedIntoAsync(ListingAuthority? listed, ListingAuthority? submitted, CancellationToken cancellationToken)
    {
        if (listed?.Kind != ListingAuthority.GitHub || submitted?.Kind != ListingAuthority.GitHub)
            return false;

        var reply = await SendAsync(HttpMethod.Get, $"{Api}/repos/{listed.Target}", null, ListingPublishStep.Ownership, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return false;

        var repository = Parse<RepositoryDto>(Ensure(reply, ListingPublishStep.Ownership), ListingPublishStep.Ownership);
        return !repository.Fork && string.Equals(repository.FullName, submitted.Target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A marker that names only the login covers every listing of the repository.</summary>
    private bool MarkerNames(string text, string id, string login)
    {
        AuthoredTable marker;
        try
        {
            marker = _format.Read(text);
        }
        catch (FormatException)
        {
            return false;
        }

        var claimed = IsSet(marker["login"]) ? marker["login"] : marker["account"];
        var identifier = IsSet(marker["id"]) ? marker["id"] : marker["listing"];
        return claimed is string account
            && string.Equals(account, login, StringComparison.OrdinalIgnoreCase)
            && (identifier is not string named || string.Equals(named, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSet(object? value) => value switch
    {
        null => false,
        string text => text.Length > 0,
        long number => number != 0,
        double number => number != 0,
        bool flag => flag,
        AuthoredTable table => table.Count > 0,
        IReadOnlyList<object> list => list.Count > 0,
        _ => true,
    };

    private static ListingOwnership NotVerified(ListingOwnershipProblem problem, string repository) =>
        new(ListingOwnershipState.NotVerified, Problem: problem, Repository: repository);

    private async Task<OpenPullRequest?> FindOpenPullRequestAsync(string login, string path, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.FindPullRequest;
        for (var page = 1; page <= MaxPages; page++)
        {
            var pulls = await GetAsync<List<PullDto>>($"{Api}/repos/{Upstream}/pulls?state=open&per_page=100&page={page}", step, cancellationToken).ConfigureAwait(false);
            foreach (var pull in pulls.Where(pull => string.Equals(pull.User?.Login, login, StringComparison.OrdinalIgnoreCase) && pull.Head?.Repo is not null))
            {
                var files = await FilesAsync(pull.Number, cancellationToken).ConfigureAwait(false);
                if (files.Contains(path, StringComparer.Ordinal))
                    return new OpenPullRequest(pull, files.All(file => file == path));
            }

            if (pulls.Count < 100)
                break;
        }

        return null;
    }

    private async Task<List<string>> FilesAsync(int number, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var files = await GetAsync<List<PullFileDto>>($"{Api}/repos/{Upstream}/pulls/{number}/files?per_page=100&page={page}", ListingPublishStep.FindPullRequest, cancellationToken).ConfigureAwait(false);
            names.AddRange(files.Select(file => file.Filename));
            if (files.Count < 100)
                break;
        }

        return names;
    }

    private async Task<ListingPullRequest> CommitToOpenAsync(PullDto pull, ListingSubmission submission, CancellationToken cancellationToken)
    {
        var url = PullRequestUrl(pull, ListingPublishStep.Commit);
        var repository = pull.Head!.Repo!.FullName;
        var branch = pull.Head.Ref;
        var current = await ReadFileAsync(repository, submission.Path, branch, ListingPublishStep.Commit, cancellationToken).ConfigureAwait(false);
        if (current?.Text == submission.Text)
            return new ListingPullRequest(pull.Number, url, ListingPublishOutcome.Unchanged);

        await PutFileAsync(repository, branch, submission, current?.Sha, cancellationToken).ConfigureAwait(false);
        return new ListingPullRequest(pull.Number, url, ListingPublishOutcome.Updated);
    }

    /// <summary>
    /// The fork of content-index among the repositories of the App's installation on the author's own account.
    /// Without one, a read without the token tells a missing fork from a fork without the App.
    /// </summary>
    private async Task<Fork> FindForkAsync(string login, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Fork;
        if (await FindInstallationAsync(login, cancellationToken).ConfigureAwait(false) is { } installation)
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var repositories = await GetAsync<InstallationRepositoriesDto>($"{Api}/user/installations/{installation}/repositories?per_page=100&page={page}", step, cancellationToken).ConfigureAwait(false);
                foreach (var candidate in repositories.Repositories.Where(repository => repository.Fork))
                {
                    var repository = await GetAsync<RepositoryDto>($"{Api}/repos/{candidate.FullName}", step, cancellationToken).ConfigureAwait(false);
                    if (IsForkOfUpstream(repository) && ForkOf(repository) is { } fork)
                        return fork;
                }

                if (repositories.Repositories.Count < 100)
                    break;
            }
        }

        var reply = await SendAsync(HttpMethod.Get, $"{Api}/repos/{login}/content-index", null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status != HttpStatusCode.NotFound && Parse<RepositoryDto>(Ensure(reply, step), step) is var named && IsForkOfUpstream(named))
            throw new ListingPublishException(ListingPublishFailure.AppNotOnFork, step, named.FullName, repositoryId: named.Id > 0 ? named.Id : null);

        throw new ListingPublishException(ListingPublishFailure.NoFork, step);
    }

    private async Task<long?> FindInstallationAsync(string login, CancellationToken cancellationToken)
    {
        for (var page = 1; page <= MaxPages; page++)
        {
            var installations = await GetAsync<InstallationsDto>($"{Api}/user/installations?per_page=100&page={page}", ListingPublishStep.Fork, cancellationToken).ConfigureAwait(false);
            if (installations.Installations.FirstOrDefault(installation => string.Equals(installation.Account?.Login, login, StringComparison.OrdinalIgnoreCase)) is { } own)
                return own.Id;

            if (installations.Installations.Count < 100)
                break;
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

    /// <summary>Creates listing-&lt;id&gt; at <paramref name="sha"/>, or the first free listing-&lt;id&gt;-N.</summary>
    private async Task<string> CreateBranchAsync(Fork fork, string id, string sha, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Branch;
        var baseName = "listing-" + id.ToLowerInvariant();
        var mergedUpstream = false;
        for (var number = 1; number <= MaxBranchNumber; number++)
        {
            var name = number == 1 ? baseName : $"{baseName}-{number.ToString(CultureInfo.InvariantCulture)}";
            var existing = await SendAsync(HttpMethod.Get, $"{Api}/repos/{fork.FullName}/git/ref/heads/{name}", null, step, cancellationToken).ConfigureAwait(false);
            if (existing.Status == HttpStatusCode.OK)
                continue;
            if (existing.Status != HttpStatusCode.NotFound)
                Ensure(existing, step);

            var body = new Dictionary<string, object> { ["ref"] = "refs/heads/" + name, ["sha"] = sha };
            var created = await SendAsync(HttpMethod.Post, $"{Api}/repos/{fork.FullName}/git/refs", body, step, cancellationToken).ConfigureAwait(false);
            if (created.Status == HttpStatusCode.Created)
                return name;
            if (created.Status != HttpStatusCode.UnprocessableEntity)
                Ensure(created, step);
            if (created.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true)
                continue;
            if (mergedUpstream)
                Ensure(created, step);

            // The commit of main is not in the fork yet, so the fork catches up once.
            var merge = new Dictionary<string, object> { ["branch"] = fork.DefaultBranch };
            Ensure(await SendAsync(HttpMethod.Post, $"{Api}/repos/{fork.FullName}/merge-upstream", merge, step, cancellationToken).ConfigureAwait(false), step);
            mergedUpstream = true;
            number--;
        }

        throw new ListingPublishException(ListingPublishFailure.Refused, step, $"{baseName} to {baseName}-{MaxBranchNumber} are taken");
    }

    private async Task PutFileAsync(string repository, string branch, ListingSubmission submission, string? sha, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object>
        {
            ["message"] = submission.IsEdit ? $"Update {submission.Id}" : $"List {submission.Id}",
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(submission.Text)),
            ["branch"] = branch,
        };
        if (sha is not null)
            body["sha"] = sha;

        Ensure(await SendAsync(HttpMethod.Put, $"{Api}/repos/{repository}/contents/{submission.Path}", body, ListingPublishStep.Commit, cancellationToken).ConfigureAwait(false), ListingPublishStep.Commit);
    }

    /// <summary>The file and its blob sha at <paramref name="reference"/>, or null when it is not there.</summary>
    private async Task<RepositoryFile?> ReadFileAsync(string repository, string path, string? reference, ListingPublishStep step, CancellationToken cancellationToken, bool anonymous = false)
    {
        var url = $"{Api}/repos/{repository}/contents/{path}" + (reference is null ? string.Empty : "?ref=" + Uri.EscapeDataString(reference));
        var reply = await SendAsync(HttpMethod.Get, url, null, step, cancellationToken, anonymous).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return null;

        var file = Parse<ContentDto>(Ensure(reply, step), step);
        if (file.Encoding != "base64" || file.Sha.Length == 0)
            throw Unexpected(step);

        try
        {
            return new RepositoryFile(file.Sha, StrictUtf8.GetString(Convert.FromBase64String(file.Content)));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return new RepositoryFile(file.Sha, null);
        }
    }

    private async Task<T> GetAsync<T>(string url, ListingPublishStep step, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(HttpMethod.Get, url, null, step, cancellationToken).ConfigureAwait(false);
        return Parse<T>(Ensure(reply, step), step);
    }

    /// <summary>The App has no Commit statuses permission, so GitHub may refuse the token on a public read. A refusal is read again without the token.</summary>
    private async Task<T> GetPublicAsync<T>(string url, ListingPublishStep step, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(HttpMethod.Get, url, null, step, cancellationToken).ConfigureAwait(false);
        if (reply is { Status: HttpStatusCode.Forbidden, RetryAt: null })
            reply = await SendAsync(HttpMethod.Get, url, null, step, cancellationToken, anonymous: true).ConfigureAwait(false);

        return Parse<T>(Ensure(reply, step), step);
    }

    /// <summary>
    /// Sends through the session, or without the token when <paramref name="anonymous"/>,
    /// and follows a redirect of GitHub, which the session's client does not follow.
    /// </summary>
    private async Task<Reply> SendAsync(HttpMethod method, string url, object? body, ListingPublishStep step, CancellationToken cancellationToken, bool anonymous = false)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body);
        var target = new Uri(url);
        for (var redirects = 0; ; redirects++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(method, target);
            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (anonymous)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", BoreaReleaseCheck.ApiVersion);
            }

            try
            {
                using var response = anonymous
                    ? await _http.SendAsync(request, cancellationToken).ConfigureAwait(false)
                    : await _session.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized && !anonymous)
                    throw new ListingPublishException(ListingPublishFailure.SignedOut, step);

                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location && redirects < MaxRedirects)
                {
                    target = location.IsAbsoluteUri ? location : new Uri(target, location);
                    if (!target.AbsoluteUri.StartsWith(Api + "/", StringComparison.OrdinalIgnoreCase))
                        throw Unexpected(step);
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new Reply(response.StatusCode, text, MessageOf(text), RetryAtOf(response, text));
            }
            catch (InvalidOperationException exception) when (!anonymous)
            {
                throw new ListingPublishException(ListingPublishFailure.SignedOut, step, innerException: exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                throw new ListingPublishException(ListingPublishFailure.NetworkError, step, innerException: exception);
            }
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static string Ensure(Reply reply, ListingPublishStep step)
    {
        if ((int)reply.Status is >= 200 and < 300)
            return reply.Body;

        var failure = reply.Status switch
        {
            _ when reply.RetryAt is not null => ListingPublishFailure.RateLimited,
            HttpStatusCode.Forbidden => ListingPublishFailure.Forbidden,
            HttpStatusCode.NotFound => ListingPublishFailure.NotFound,
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict => ListingPublishFailure.Refused,
            _ => ListingPublishFailure.UnexpectedResponse,
        };
        var detail = failure == ListingPublishFailure.UnexpectedResponse ? $"HTTP {(int)reply.Status}" : reply.Message;
        throw new ListingPublishException(failure, step, detail, reply.RetryAt);
    }

    private DateTimeOffset? RetryAtOf(HttpResponseMessage response, string body)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return null;

        var now = _time.GetUtcNow();
        if (response.Headers.RetryAfter is { } retryAfter)
            return retryAfter.Delta is { } delta ? now + delta : retryAfter.Date ?? now + SecondaryLimitWait;

        if (Header(response, "x-ratelimit-remaining") == "0"
            && long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var reset))
        {
            return DateTimeOffset.FromUnixTimeSeconds(reset);
        }

        return response.StatusCode == HttpStatusCode.TooManyRequests || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            ? now + SecondaryLimitWait
            : null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? MessageOf(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorDto>(body, Json);
            var details = error?.Errors?.Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object when item.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String => message.GetString(),
                _ => null,
            }) ?? [];
            var parts = new[] { error?.Message }.Concat(details).Where(part => !string.IsNullOrWhiteSpace(part));
            var message = string.Join(". ", parts);
            return message.Length > 0 ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T Parse<T>(string body, ListingPublishStep step)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json) ?? throw Unexpected(step);
        }
        catch (JsonException exception)
        {
            throw new ListingPublishException(ListingPublishFailure.UnexpectedResponse, step, innerException: exception);
        }
    }

    private static Uri PullRequestUrl(PullDto pull, ListingPublishStep step) =>
        Uri.TryCreate(pull.HtmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && pull.Number > 0 ? url : throw Unexpected(step);

    private static ListingPublishException Unexpected(ListingPublishStep step) => new(ListingPublishFailure.UnexpectedResponse, step);

    private sealed record Reply(HttpStatusCode Status, string Body, string? Message, DateTimeOffset? RetryAt);

    private sealed record Fork(string FullName, string Owner, string DefaultBranch);

    private sealed record RepositoryFile(string Sha, string? Text);

    private sealed record OpenPullRequest(PullDto Pull, bool OnlyThisFile);

    private sealed class UserDto
    {
        public long Id { get; set; }
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

    private sealed class RefDto
    {
        public RefObjectDto? Object { get; set; }
    }

    private sealed class RefObjectDto
    {
        public string? Sha { get; set; }
    }

    private sealed class ContentDto
    {
        public string Sha { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string Encoding { get; set; } = string.Empty;
    }

    private sealed class TopicsDto
    {
        public List<string> Names { get; set; } = [];
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

    private sealed class ErrorDto
    {
        public string? Message { get; set; }

        public List<JsonElement>? Errors { get; set; }
    }

    private sealed class SpaceDockModDto
    {
        public JsonElement? Id { get; set; }

        public long? GameId { get; set; }

        public string? SourceCode { get; set; }

        public bool Error { get; set; }
    }
}
