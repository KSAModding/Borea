using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;

namespace Borea.Network.GitHub;

/// <summary>
/// The REST API of GitHub, through the signed-in session or without the token, and GraphQL queries through the session. It follows redirects only inside https://api.github.com.
/// It never commits to or moves the base branch of a KSAModding repository directly, because the token of a steward bypasses the ruleset of that branch.
/// </summary>
internal sealed class GitHubApi
{
    internal const string Root = "https://api.github.com";

    internal const string Organization = "KSAModding";

    internal const int PageSize = 100;

    internal static readonly TimeSpan SecondaryLimitWait = TimeSpan.FromMinutes(1);

    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly JsonSerializerOptions GraphQlJson = new(JsonSerializerDefaults.Web);

    private const int MaxRedirects = 5;

    private const int MaxPages = 10;

    private const int MaxBranchNumber = 100;

    private static readonly string[] CommitBranchPrefixes = ["steward/", "listing-"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IGitHubSession _session;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string[] _protectedBranches;

    /// <param name="http">Sends the requests without the token.</param>
    /// <param name="baseBranch">The base branch of the pull requests, protected like main. Null takes <see cref="ListingPullRequestLinks.Branch"/>.</param>
    public GitHubApi(IGitHubSession session, HttpClient http, TimeProvider? time = null, string? baseBranch = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? TimeProvider.System;
        _protectedBranches = [ListingPullRequestLinks.DefaultBranch, baseBranch ?? ListingPullRequestLinks.Branch];
    }

    /// <summary>
    /// Sends through the session, or without the token when <paramref name="anonymous"/>, and follows a redirect of GitHub,
    /// which the session's client does not follow. Any answer comes back, and only a 401 to the token throws.
    /// </summary>
    /// <exception cref="GitHubApiException">Signed out, not reached, a redirect away from the API, or a write that the branch guard refuses.</exception>
    public async Task<GitHubReply> SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken, bool anonymous = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        var json = body is null ? null : JsonSerializer.Serialize(body);
        var target = new Uri(url);
        for (var redirects = 0; ; redirects++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RefusalOf(method, target, json) is { } refusal)
                throw new GitHubApiException(GitHubApiFailure.ProtectedBranch, refusal);

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
                    throw new GitHubApiException(GitHubApiFailure.SignedOut, status: response.StatusCode);

                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location && redirects < MaxRedirects)
                {
                    target = location.IsAbsoluteUri ? location : new Uri(target, location);
                    if (!target.AbsoluteUri.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
                        throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, status: response.StatusCode);
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new GitHubReply(response.StatusCode, text, MessageOf(text), RetryAtOf(response, text));
            }
            catch (InvalidOperationException exception) when (!anonymous)
            {
                throw new GitHubApiException(GitHubApiFailure.SignedOut, innerException: exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                throw new GitHubApiException(GitHubApiFailure.NetworkError, innerException: exception);
            }
        }
    }

    /// <exception cref="GitHubApiException">The request failed, or GitHub did not answer with a success.</exception>
    public async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken, bool anonymous = false)
    {
        var reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken, anonymous).ConfigureAwait(false);
        return Parse<T>(Ensure(reply));
    }

    /// <summary>
    /// Reads public data. GitHub can refuse the token on it when the App lacks the permission, for example Commit statuses,
    /// so a refusal that is no rate limit is read again without the token.
    /// </summary>
    /// <exception cref="GitHubApiException">The request failed, or GitHub did not answer with a success.</exception>
    public async Task<T> GetPublicAsync<T>(string url, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
        if (reply is { Status: HttpStatusCode.Forbidden, RetryAt: null })
            reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken, anonymous: true).ConfigureAwait(false);

        return Parse<T>(Ensure(reply));
    }

    /// <summary>
    /// Sends a GraphQL query through the session and gives its data. GraphQL answers an error with a success, so an error in the answer throws.
    /// Only a query goes, because the branch guard knows only the paths of the REST API.
    /// </summary>
    /// <exception cref="ArgumentException">The document is no query.</exception>
    /// <exception cref="GitHubApiException">The request failed, or GitHub answered with an error or without data.</exception>
    public async Task<T> QueryAsync<T>(string query, IReadOnlyDictionary<string, object> variables, CancellationToken cancellationToken)
    {
        if (!query.TrimStart().StartsWith("query", StringComparison.Ordinal))
            throw new ArgumentException("Borea sends only a GraphQL query.", nameof(query));

        var body = new Dictionary<string, object> { ["query"] = query, ["variables"] = variables };
        var reply = await SendAsync(HttpMethod.Post, Root + "/graphql", body, cancellationToken).ConfigureAwait(false);
        GraphQlReply<T> answer;
        try
        {
            answer = JsonSerializer.Deserialize<GraphQlReply<T>>(Ensure(reply), GraphQlJson) ?? throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
        }
        catch (JsonException exception)
        {
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, innerException: exception);
        }

        if (answer.Errors is [var error, ..])
        {
            var failure = error.Type switch
            {
                "NOT_FOUND" => GitHubApiFailure.NotFound,
                "FORBIDDEN" => GitHubApiFailure.Forbidden,
                "RATE_LIMITED" => GitHubApiFailure.RateLimited,
                _ => GitHubApiFailure.UnexpectedResponse,
            };
            var retryAt = failure == GitHubApiFailure.RateLimited ? _time.GetUtcNow() + SecondaryLimitWait : (DateTimeOffset?)null;
            throw new GitHubApiException(failure, error.Message, retryAt, reply.Status);
        }

        return answer.Data ?? throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
    }

    /// <summary>The file and its blob sha at <paramref name="reference"/>, or null when it is not there. The text is null when the file is no UTF-8 text.</summary>
    /// <param name="reference">A branch or commit, or null for the default branch.</param>
    /// <exception cref="GitHubApiException">The request failed, or GitHub did not answer with the file.</exception>
    public async Task<GitHubFile?> ReadFileAsync(string repository, string path, string? reference, CancellationToken cancellationToken, bool anonymous = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        var url = $"{Root}/repos/{repository}/contents/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}"
            + (reference is null ? string.Empty : "?ref=" + Uri.EscapeDataString(reference));
        var reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken, anonymous).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return null;

        var file = Parse<ContentDto>(Ensure(reply));
        if (file.Encoding != "base64" || file.Sha.Length == 0)
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);

        try
        {
            return new GitHubFile(file.Sha, StrictUtf8.GetString(Convert.FromBase64String(file.Content)));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return new GitHubFile(file.Sha, null);
        }
    }

    /// <summary>Creates the branch <paramref name="name"/> at <paramref name="sha"/>, or the first free <paramref name="name"/>-N when it is taken.</summary>
    /// <returns>The name of the branch it created.</returns>
    /// <exception cref="GitHubApiException">A request failed, or every name up to <paramref name="name"/>-<see cref="MaxBranchNumber"/> is taken.</exception>
    public async Task<string> CreateFreeBranchAsync(string repository, string name, string sha, CancellationToken cancellationToken)
    {
        for (var number = 1; number <= MaxBranchNumber; number++)
        {
            var branch = number == 1 ? name : $"{name}-{number.ToString(CultureInfo.InvariantCulture)}";
            var existing = await SendAsync(HttpMethod.Get, $"{Root}/repos/{repository}/git/ref/heads/{branch}", null, cancellationToken).ConfigureAwait(false);
            if (existing.Status == HttpStatusCode.OK)
                continue;
            if (existing.Status != HttpStatusCode.NotFound)
                Ensure(existing);

            var body = new Dictionary<string, object> { ["ref"] = "refs/heads/" + branch, ["sha"] = sha };
            var created = await SendAsync(HttpMethod.Post, $"{Root}/repos/{repository}/git/refs", body, cancellationToken).ConfigureAwait(false);
            if (created.Status == HttpStatusCode.Created)
                return branch;
            if (created.Status == HttpStatusCode.UnprocessableEntity && created.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            Ensure(created);
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
        }

        throw new GitHubApiException(GitHubApiFailure.Refused, $"{name} to {name}-{MaxBranchNumber} are taken");
    }

    /// <summary>
    /// Commits the files onto <paramref name="parent"/> in one commit that no branch holds yet, so a failure halfway leaves no branch behind.
    /// The branch guard checks the ref that later points at it.
    /// </summary>
    /// <param name="files">The path and the new UTF-8 text of each file.</param>
    /// <returns>The sha of the new commit.</returns>
    /// <exception cref="GitHubApiException">A request failed, or GitHub answered without a sha.</exception>
    public async Task<string> CommitAsync(string repository, string parent, IReadOnlyCollection<(string Path, string Text)> files, string message, CancellationToken cancellationToken)
    {
        var git = $"{Root}/repos/{repository}/git";
        var parentTree = (await GetAsync<CommitDto>($"{git}/commits/{parent}", cancellationToken).ConfigureAwait(false)).Tree?.Sha;
        var entries = new List<Dictionary<string, object>>();
        foreach (var (path, text) in files)
        {
            var blob = new Dictionary<string, object> { ["content"] = Convert.ToBase64String(StrictUtf8.GetBytes(text)), ["encoding"] = "base64" };
            var sha = await CreateAsync($"{git}/blobs", blob, cancellationToken).ConfigureAwait(false);
            entries.Add(new Dictionary<string, object> { ["path"] = path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = sha });
        }

        var tree = await CreateAsync($"{git}/trees", new Dictionary<string, object>
        {
            ["base_tree"] = parentTree is { Length: > 0 } ? parentTree : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse),
            ["tree"] = entries,
        }, cancellationToken).ConfigureAwait(false);
        return await CreateAsync($"{git}/commits", new Dictionary<string, object> { ["message"] = message, ["tree"] = tree, ["parents"] = new[] { parent } }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a git object and gives its sha.</summary>
    private async Task<string> CreateAsync(string url, Dictionary<string, object> body, CancellationToken cancellationToken)
    {
        var created = Parse<ShaDto>(Ensure(await SendAsync(HttpMethod.Post, url, body, cancellationToken).ConfigureAwait(false)));
        return created.Sha is { Length: > 0 } sha ? sha : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
    }

    /// <summary>The items of a list endpoint that answers with a JSON array, page by page, up to the first page with fewer than <see cref="PageSize"/> items.</summary>
    /// <param name="url">The endpoint with its own query, without per_page and page.</param>
    public IAsyncEnumerable<T> GetPagesAsync<T>(string url, CancellationToken cancellationToken, bool anonymous = false) =>
        GetPagesAsync<List<T>, T>(url, page => page, cancellationToken, anonymous);

    /// <summary>The same for an endpoint that wraps the array in an object, which <paramref name="items"/> opens.</summary>
    public async IAsyncEnumerable<TItem> GetPagesAsync<TPage, TItem>(string url, Func<TPage, IReadOnlyCollection<TItem>> items, [EnumeratorCancellation] CancellationToken cancellationToken, bool anonymous = false)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        for (var page = 1; page <= MaxPages; page++)
        {
            var found = items(await GetAsync<TPage>($"{url}{separator}per_page={PageSize}&page={page.ToString(CultureInfo.InvariantCulture)}", cancellationToken, anonymous).ConfigureAwait(false));
            foreach (var item in found)
                yield return item;

            if (found.Count < PageSize)
                yield break;
        }
    }

    /// <summary>
    /// The open issues of a public repository without the token, oldest first, and without the pull requests that the endpoint lists too.
    /// An anonymous read does not depend on a permission of the App.
    /// </summary>
    /// <param name="query">More of the query, such as labels=watcher, or null.</param>
    /// <exception cref="GitHubApiException">The request failed, or GitHub answered with an issue that has no https link.</exception>
    public async IAsyncEnumerable<GitHubIssue> GetOpenIssuesAsync(string repository, string? query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var url = $"{Root}/repos/{repository}/issues?state=open&sort=created&direction=asc" + (query is null ? string.Empty : "&" + query);
        await foreach (var issue in GetPagesAsync<IssueDto>(url, cancellationToken, anonymous: true).ConfigureAwait(false))
        {
            if (issue.PullRequest is null)
                yield return IssueOf(issue);
        }
    }

    /// <summary>
    /// The <see cref="PageSize"/> closed issues of a public repository that changed last, the last changed first, without the token.
    /// The search leaves out the pull requests, which the issues endpoint lists too. It reads one page only, because an older issue is rarely acted on
    /// and the anonymous search allows few requests. The search index can lag some seconds behind a change.
    /// </summary>
    /// <param name="query">More search terms joined with +, such as in:title+Takedown, or null.</param>
    /// <exception cref="GitHubApiException">The request failed, or GitHub answered with an issue that has no https link.</exception>
    public async Task<IReadOnlyList<GitHubIssue>> GetRecentlyClosedIssuesAsync(string repository, string? query, CancellationToken cancellationToken)
    {
        var terms = $"repo:{repository}+is:issue+is:closed" + (query is null ? string.Empty : "+" + query);
        var url = $"{Root}/search/issues?q={terms}&sort=updated&order=desc&per_page={PageSize}&page=1";
        var found = await GetAsync<SearchIssuesDto>(url, cancellationToken, anonymous: true).ConfigureAwait(false);
        return found.Items.Where(issue => issue.PullRequest is null).Select(IssueOf).ToList();
    }

    /// <summary>
    /// The issue as GitHub has it now, read with the token and again without it when GitHub refuses the token, because the App can lack the Issues permission.
    /// A moved issue comes back with its new link.
    /// </summary>
    /// <exception cref="GitHubApiException">The request failed, the issue is gone, it is a pull request, or it has no https link.</exception>
    public async Task<GitHubIssue> GetIssueAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var url = $"{Root}/repos/{repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}";
        var reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
        if (reply is { Status: HttpStatusCode.Forbidden, RetryAt: null })
            reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.Gone)
            throw new GitHubApiException(GitHubApiFailure.NotFound, reply.Message, status: reply.Status);

        var issue = Parse<IssueDto>(Ensure(reply));
        return issue.PullRequest is null ? IssueOf(issue) : throw new GitHubApiException(GitHubApiFailure.NotFound);
    }

    private static GitHubIssue IssueOf(IssueDto issue)
    {
        if (!Uri.TryCreate(issue.HtmlUrl, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps || issue.Number <= 0)
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);

        return new GitHubIssue(issue.Number, link, issue.Title, issue.Body, issue.User?.Login, issue.User?.Type, issue.CreatedAt, issue.UpdatedAt)
        {
            AuthorId = issue.User?.Id,
            IsClosed = issue.State == "closed",
            StateReason = issue.StateReason,
            Closed = issue.ClosedAt,
            Comments = issue.Comments,
        };
    }

    /// <summary>The body of a success.</summary>
    /// <exception cref="GitHubApiException">Any other answer, with GitHub's message and the rate-limit time.</exception>
    public static string Ensure(GitHubReply reply)
    {
        if ((int)reply.Status is >= 200 and < 300)
            return reply.Body;

        var failure = reply.Status switch
        {
            _ when reply.RetryAt is not null => GitHubApiFailure.RateLimited,
            HttpStatusCode.Forbidden => GitHubApiFailure.Forbidden,
            HttpStatusCode.NotFound => GitHubApiFailure.NotFound,
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict => GitHubApiFailure.Refused,
            _ => GitHubApiFailure.UnexpectedResponse,
        };
        var detail = failure == GitHubApiFailure.UnexpectedResponse ? $"HTTP {(int)reply.Status}" : reply.Message;
        throw new GitHubApiException(failure, detail, reply.RetryAt, reply.Status);
    }

    /// <exception cref="GitHubApiException">The body is not <typeparamref name="T"/>.</exception>
    public static T Parse<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json) ?? throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
        }
        catch (JsonException exception)
        {
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, innerException: exception);
        }
    }

    /// <summary>
    /// Why the guard refuses a write to a repository of the organization, or null. A commit, and a ref that is created or moved, go only to a steward/ or
    /// listing- branch that is not protected, and a deleted ref, a merge or a branch change never touches a protected branch.
    /// A repository addressed by its id counts as one of the organization.
    /// </summary>
    private string? RefusalOf(HttpMethod method, Uri target, string? json)
    {
        if (method == HttpMethod.Get || method == HttpMethod.Head)
            return null;

        var segments = target.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (RepositoryPathOf(segments) is not { } path)
            return null;

        return path switch
        {
            ["contents", ..] => CommitRefusal(FieldOf(json, "branch")),
            ["git", "refs"] => CommitRefusal(BranchOfRef(FieldOf(json, "ref"))),
            ["git", "refs", .. var name] when method == HttpMethod.Delete => Protected(BranchOfRef(string.Join('/', name))),
            ["git", "refs", .. var name] => CommitRefusal(BranchOfRef(string.Join('/', name))),
            ["merges"] => Protected(FieldOf(json, "base")),
            ["merge-upstream"] => Protected(FieldOf(json, "branch")),
            ["branches", ..] => _protectedBranches.FirstOrDefault(branch => IsBranchPath(path[1..], branch)) is { } named ? Refused(named) : null,
            _ => null,
        };
    }

    /// <summary>The path below the repository, for a repository of the organization or one addressed by its id, or null for any other path.</summary>
    private static string[]? RepositoryPathOf(string[] segments) => segments switch
    {
        ["repos", var owner, _, ..] => string.Equals(owner, Organization, StringComparison.OrdinalIgnoreCase) ? segments[3..] : null,
        ["repositories", _, ..] => segments[2..],
        _ => null,
    };

    private string? CommitRefusal(string? branch) =>
        Protected(branch) ?? (branch is not null && CommitBranchPrefixes.Any(prefix => branch.StartsWith(prefix, StringComparison.Ordinal))
            ? null
            : $"Borea commits only to a branch that starts with {string.Join(" or ", CommitBranchPrefixes)}, not to {branch ?? "the default branch"}");

    private string? Protected(string? branch) =>
        branch is not null && _protectedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase) ? Refused(branch) : null;

    private static string Refused(string branch) => $"Borea does not write to {branch}";

    private static string? BranchOfRef(string? reference)
    {
        if (reference is null)
            return null;

        var name = reference.StartsWith("refs/", StringComparison.Ordinal) ? reference["refs/".Length..] : reference;
        return name.StartsWith("heads/", StringComparison.Ordinal) ? name["heads/".Length..] : null;
    }

    /// <summary>
    /// Whether a path under branches/ is <paramref name="branch"/> or one of its settings, for a branch name with slashes too.
    /// The segments are joined again, because a slash of the name can come escaped as %2F inside one segment.
    /// </summary>
    private static bool IsBranchPath(string[] rest, string branch)
    {
        var joined = string.Join('/', rest);
        return joined.Equals(branch, StringComparison.OrdinalIgnoreCase) || joined.StartsWith(branch + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FieldOf(string? json, string name)
    {
        if (json is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

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

    private sealed class ErrorDto
    {
        public string? Message { get; set; }

        public List<JsonElement>? Errors { get; set; }
    }

    private sealed class GraphQlReply<T>
    {
        public T? Data { get; set; }

        public List<GraphQlError>? Errors { get; set; }
    }

    private sealed class GraphQlError
    {
        public string? Type { get; set; }

        public string? Message { get; set; }
    }

    private sealed class ShaDto
    {
        public string? Sha { get; set; }
    }

    private sealed class CommitDto
    {
        public ShaDto? Tree { get; set; }
    }

    private sealed class ContentDto
    {
        public string Sha { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string Encoding { get; set; } = string.Empty;
    }

    private sealed class IssueDto
    {
        public int Number { get; set; }

        public string Title { get; set; } = string.Empty;

        public string HtmlUrl { get; set; } = string.Empty;

        public string? Body { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public string? State { get; set; }

        public string? StateReason { get; set; }

        public DateTimeOffset? ClosedAt { get; set; }

        public int Comments { get; set; }

        public IssueUserDto? User { get; set; }

        public JsonElement? PullRequest { get; set; }
    }

    private sealed class SearchIssuesDto
    {
        public List<IssueDto> Items { get; set; } = [];
    }

    private sealed class IssueUserDto
    {
        public string? Login { get; set; }

        public long? Id { get; set; }

        public string? Type { get; set; }
    }
}

/// <param name="Text">The file as UTF-8 text, or null when it is not.</param>
internal sealed record GitHubFile(string Sha, string? Text);

/// <param name="Author">The login that opened the issue, or null.</param>
/// <param name="AuthorType">The account type of the author, such as User or Bot, or null.</param>
internal sealed record GitHubIssue(int Number, Uri Url, string Title, string? Body, string? Author, string? AuthorType, DateTimeOffset Created, DateTimeOffset Updated)
{
    public bool IsClosed { get; init; }

    /// <summary>Why GitHub has the issue closed or reopened, such as completed, not_planned or reopened, or null.</summary>
    public string? StateReason { get; init; }

    public DateTimeOffset? Closed { get; init; }

    public int Comments { get; init; }

    public long? AuthorId { get; init; }
}

/// <param name="Message">GitHub's error message with its details, or null.</param>
/// <param name="RetryAt">When GitHub accepts requests again after a rate limit, or null.</param>
internal sealed record GitHubReply(HttpStatusCode Status, string Body, string? Message, DateTimeOffset? RetryAt);

internal enum GitHubApiFailure
{
    SignedOut,
    RateLimited,
    NotFound,
    Refused,
    Forbidden,
    NetworkError,
    UnexpectedResponse,

    /// <summary>The branch guard refused a write before anything was sent.</summary>
    ProtectedBranch,
}

/// <summary>A request to the GitHub API failed. The message never holds the token.</summary>
internal sealed class GitHubApiException(GitHubApiFailure failure, string? detail = null, DateTimeOffset? retryAt = null, HttpStatusCode? status = null, Exception? innerException = null)
    : Exception(failure + (detail is null ? string.Empty : ", " + detail), innerException)
{
    public GitHubApiFailure Failure { get; } = failure;

    /// <summary>What GitHub or the branch guard said.</summary>
    public string? Detail { get; } = detail;

    public DateTimeOffset? RetryAt { get; } = retryAt;

    /// <summary>The status of GitHub's answer, when there was one.</summary>
    public HttpStatusCode? Status { get; } = status;

    public StewardException ToStewardException()
    {
        var failure = Failure switch
        {
            GitHubApiFailure.SignedOut => StewardFailure.SignedOut,
            GitHubApiFailure.RateLimited => StewardFailure.RateLimited,
            GitHubApiFailure.NotFound => StewardFailure.NotFound,
            GitHubApiFailure.Refused or GitHubApiFailure.ProtectedBranch => StewardFailure.Refused,
            GitHubApiFailure.Forbidden => StewardFailure.Forbidden,
            GitHubApiFailure.NetworkError => StewardFailure.NetworkError,
            _ => StewardFailure.UnexpectedResponse,
        };
        return new StewardException(failure, Detail, RetryAt, this);
    }
}
