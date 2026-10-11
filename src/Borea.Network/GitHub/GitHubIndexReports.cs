using System.Globalization;
using Borea.Core.GitHub;
using Borea.Core.Stewardship;

namespace Borea.Network.GitHub;

/// <summary>
/// IIndexReports on the REST API without the token, because the issues of content-index are public, and IReportActions with the token
/// of the signed-in session, which GitHub refuses while the installation of the Borea App on KSAModding has no Issues permission.
/// </summary>
public sealed class GitHubIndexReports : IIndexReports, IReportActions
{
    /// <summary>The search terms for a title that holds the tag of a takedown or a dispute.</summary>
    private const string ClosedReportsQuery = "in:title+(Takedown+OR+Dispute)";

    private readonly IGitHubSession _session;
    private readonly GitHubApi _api;

    public GitHubIndexReports(IGitHubSession session, HttpClient http, TimeProvider? time = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _api = new GitHubApi(session, http, time);
    }

    public async Task<IReadOnlyList<IndexReport>> ListAsync(IndexReportFilter filter = IndexReportFilter.Open, CancellationToken cancellationToken = default)
    {
        try
        {
            // The label of the forms can be taken off a report, so the tag in the title finds it.
            var reports = new List<IndexReport>();
            if (filter == IndexReportFilter.RecentlyClosed)
            {
                // other closed issues would push the reports out of the one page that is read, and the search drops the brackets of the tags
                foreach (var issue in await _api.GetRecentlyClosedIssuesAsync(IndexReport.Repository, ClosedReportsQuery, cancellationToken).ConfigureAwait(false))
                    Add(issue);
            }
            else
            {
                await foreach (var issue in _api.GetOpenIssuesAsync(IndexReport.Repository, null, cancellationToken).ConfigureAwait(false))
                    Add(issue);
            }

            return reports;

            void Add(GitHubIssue issue)
            {
                if (IndexReport.FromIssue(issue.Number, issue.Url, issue.Title, issue.Author, issue.Created, issue.Body) is { } report)
                    reports.Add(issue.IsClosed ? report with { IsClosed = true, CloseReason = CloseReasonOf(issue.StateReason), Closed = issue.Closed } : report);
            }
        }
        catch (GitHubApiException exception)
        {
            throw exception.ToStewardException();
        }
    }

    /// <summary>
    /// Reads the installations of the Borea App that the signed-in account can see. An org owner gives the App a new permission
    /// on the installation of the org, so the installation on KSAModding says whether the Issues permission is there.
    /// </summary>
    public async Task<bool> CanActAsync(CancellationToken cancellationToken = default)
    {
        if (_session.State.Status != GitHubSessionStatus.SignedIn)
            return false;

        var owner = IndexReport.Repository[..IndexReport.Repository.IndexOf('/', StringComparison.Ordinal)];
        try
        {
            var installations = _api.GetPagesAsync<InstallationsDto, InstallationDto>($"{GitHubApi.Root}/user/installations", page => page.Installations, cancellationToken);
            await foreach (var installation in installations.ConfigureAwait(false))
            {
                if (string.Equals(installation.Account?.Login, owner, StringComparison.OrdinalIgnoreCase))
                    return installation.Permissions?.GetValueOrDefault("issues") == "write";
            }
        }
        catch (GitHubApiException)
        {
            // the steward answers on GitHub
        }

        return false;
    }

    public Task CommentAsync(IndexReport report, string comment, CancellationToken cancellationToken = default)
    {
        var body = Check(report, comment);
        return ActAsync(async () =>
        {
            await CurrentAsync(report, cancellationToken).ConfigureAwait(false);
            await PostCommentAsync(report, body, cancellationToken).ConfigureAwait(false);
        });
    }

    public Task CloseAsync(IndexReport report, IndexReportCloseReason reason, string comment, CancellationToken cancellationToken = default)
    {
        var body = Check(report, comment);
        var state = new Dictionary<string, object>
        {
            ["state"] = "closed",
            ["state_reason"] = reason == IndexReportCloseReason.Completed ? "completed" : "not_planned",
        };
        return ChangeStateAsync(report, body, closed: true, state, cancellationToken);
    }

    public Task ReopenAsync(IndexReport report, string comment, CancellationToken cancellationToken = default)
    {
        var body = Check(report, comment);
        return ChangeStateAsync(report, body, closed: false, new Dictionary<string, object> { ["state"] = "open" }, cancellationToken);
    }

    /// <summary>
    /// The comment goes first, so a failed change leaves the reason on GitHub and not a report closed or reopened without one,
    /// and a retry after such a failure finds its comment as the last one and only changes the state.
    /// </summary>
    private Task ChangeStateAsync(IndexReport report, string body, bool closed, Dictionary<string, object> state, CancellationToken cancellationToken) =>
        ActAsync(async () =>
        {
            var issue = await CurrentAsync(report, cancellationToken).ConfigureAwait(false);
            if (issue.IsClosed == closed)
                throw new ReportRefusedException(closed ? ReportRefusal.Closed : ReportRefusal.Open);

            if (!await IsLastCommentAsync(issue, body, cancellationToken).ConfigureAwait(false))
                await PostCommentAsync(report, body, cancellationToken).ConfigureAwait(false);

            GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Patch, IssueUrlOf(report), state, cancellationToken).ConfigureAwait(false));
        });

    private static string Check(IndexReport report, string comment)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!IsReportUrl(report.Url, report.Number))
            throw new ArgumentException($"{report.Url} is no report of {IndexReport.Repository}.", nameof(report));
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("The action needs a comment.", nameof(comment));

        return comment.Trim();
    }

    private static async Task ActAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (GitHubApiException exception)
        {
            throw exception.ToStewardException();
        }
    }

    /// <summary>The report as GitHub has it now. A report that was moved to another repository is not found, because a write would follow it there.</summary>
    private async Task<GitHubIssue> CurrentAsync(IndexReport report, CancellationToken cancellationToken)
    {
        var issue = await _api.GetIssueAsync(IndexReport.Repository, report.Number, cancellationToken).ConfigureAwait(false);
        return IsReportUrl(issue.Url, report.Number) ? issue : throw new GitHubApiException(GitHubApiFailure.NotFound);
    }

    private async Task PostCommentAsync(IndexReport report, string body, CancellationToken cancellationToken) =>
        GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Post, IssueUrlOf(report) + "/comments", new Dictionary<string, object> { ["body"] = body }, cancellationToken).ConfigureAwait(false));

    /// <summary>Whether the last comment on the issue is <paramref name="body"/> of the signed-in account. Only the last page of the comments is read.</summary>
    private async Task<bool> IsLastCommentAsync(GitHubIssue issue, string body, CancellationToken cancellationToken)
    {
        if (issue.Comments <= 0 || _session.State.Login is not { } me)
            return false;

        var page = ((issue.Comments - 1) / GitHubApi.PageSize) + 1;
        var url = $"{IssueUrlOf(issue.Number)}/comments?per_page={GitHubApi.PageSize}&page={page.ToString(CultureInfo.InvariantCulture)}";
        var comments = await _api.GetPublicAsync<List<CommentDto>>(url, cancellationToken).ConfigureAwait(false);
        return comments is [.., var last]
            && string.Equals(last.User?.Login, me, StringComparison.OrdinalIgnoreCase)
            && last.Body is { } text
            && string.Equals(text.ReplaceLineEndings("\n").Trim(), body.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static IndexReportCloseReason? CloseReasonOf(string? stateReason) => stateReason switch
    {
        "completed" => IndexReportCloseReason.Completed,
        "not_planned" => IndexReportCloseReason.NotPlanned,
        _ => null,
    };

    private static string IssueUrlOf(IndexReport report) => IssueUrlOf(report.Number);

    private static string IssueUrlOf(int number) => $"{GitHubApi.Root}/repos/{IndexReport.Repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}";

    private static bool IsReportUrl(Uri url, int number) =>
        string.Equals(url.AbsoluteUri, $"https://github.com/{IndexReport.Repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase);

    private sealed class InstallationsDto
    {
        public List<InstallationDto> Installations { get; set; } = [];
    }

    private sealed class InstallationDto
    {
        public InstallationAccountDto? Account { get; set; }

        public Dictionary<string, string>? Permissions { get; set; }
    }

    private sealed class InstallationAccountDto
    {
        public string? Login { get; set; }
    }

    private sealed class CommentDto
    {
        public string? Body { get; set; }

        public CommentUserDto? User { get; set; }
    }

    private sealed class CommentUserDto
    {
        public string? Login { get; set; }
    }
}
