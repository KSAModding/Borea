using System.Globalization;
using System.Net;
using Borea.Core.Stewardship;

namespace Borea.Network.GitHub;

public sealed partial class GitHubPullRequestReviews : IPullRequestActions
{
    private const string ReviewDecisionQuery =
        "query($owner: String!, $name: String!, $number: Int!) { repository(owner: $owner, name: $name) { pullRequest(number: $number) { reviewDecision } } }";

    public async Task ReviewAsync(PullRequestReview pullRequest, PullRequestReviewKind kind, string? body, CancellationToken cancellationToken = default)
    {
        var pulls = PullsUrlOf(pullRequest);
        if (PullRequestMerge.NeedsBody(kind) && string.IsNullOrWhiteSpace(body))
            throw new ArgumentException($"A {kind} review needs a text.", nameof(body));

        await StewardActions.ActAsync(async () =>
        {
            await EnsureUnchangedAsync(pulls, pullRequest, cancellationToken).ConfigureAwait(false);
            var review = new Dictionary<string, object>
            {
                ["commit_id"] = pullRequest.HeadCommit,
                ["event"] = kind switch
                {
                    PullRequestReviewKind.Approve => "APPROVE",
                    PullRequestReviewKind.RequestChanges => "REQUEST_CHANGES",
                    _ => "COMMENT",
                },
            };
            if (!string.IsNullOrWhiteSpace(body))
                review["body"] = body.Trim();

            GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Post, pulls + "/reviews", review, cancellationToken).ConfigureAwait(false));
        }).ConfigureAwait(false);
    }

    public async Task MergeAsync(PullRequestReview pullRequest, bool skipRequiredReview, CancellationToken cancellationToken = default)
    {
        var pulls = PullsUrlOf(pullRequest);
        await StewardActions.ActAsync(async () =>
        {
            var pull = await EnsureUnchangedAsync(pulls, pullRequest, cancellationToken).ConfigureAwait(false);
            var comments = CommentsAsync(pullRequest.Repository, pullRequest.Number, cancellationToken);
            var validate = ValidateAsync(pullRequest.Repository, pullRequest.HeadCommit, cancellationToken);
            var decision = ReviewDecisionAsync(pullRequest.Repository, pullRequest.Number, cancellationToken);
            await Task.WhenAll(comments, validate, decision).ConfigureAwait(false);

            var (status, validateFailure) = validate.Result;
            if (validateFailure is not null)
                throw validateFailure;

            var now = pullRequest with { State = PullRequestState.Open, IsDraft = pull.Draft, Validate = status, ValidateFailure = null, Verdict = IndexVerdict.Find(pullRequest.Repository, comments.Result) };
            if (PullRequestMerge.BlockersOf(now) is [var blocker, ..])
                throw new PullRequestRefusedException(blocker);
            if (decision.Result == "REVIEW_REQUIRED" && !skipRequiredReview)
                throw new PullRequestRefusedException(PullRequestRefusal.ReviewRequired);

            // sha makes GitHub refuse the merge with 409 when a commit arrived after the reads above
            var merge = new Dictionary<string, object> { ["sha"] = pullRequest.HeadCommit, ["merge_method"] = PullRequestMerge.Method };
            var reply = await _api.SendAsync(HttpMethod.Put, pulls + "/merge", merge, cancellationToken).ConfigureAwait(false);
            if (reply.Status == HttpStatusCode.Conflict)
                throw new PullRequestRefusedException(PullRequestRefusal.Changed);
            if (reply.Status == HttpStatusCode.MethodNotAllowed)
                throw new GitHubApiException(GitHubApiFailure.Refused, reply.Message, status: reply.Status);
            if (!GitHubApi.Parse<MergeDto>(GitHubApi.Ensure(reply)).Merged)
                throw new GitHubApiException(GitHubApiFailure.Refused, reply.Message, status: reply.Status);
        }).ConfigureAwait(false);
    }

    public async Task CloseAsync(PullRequestReview pullRequest, string comment, CancellationToken cancellationToken = default)
    {
        var pulls = PullsUrlOf(pullRequest);
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("Closing needs a comment.", nameof(comment));

        await StewardActions.ActAsync(async () =>
        {
            await EnsureUnchangedAsync(pulls, pullRequest, cancellationToken).ConfigureAwait(false);

            // the comment goes first, so a failed close leaves the reason on GitHub and not a pull request closed without one,
            // and a retry after such a failure finds its comment as the last one and only closes
            var body = comment.Trim();
            var posted = await CommentsAsync(pullRequest.Repository, pullRequest.Number, cancellationToken).ConfigureAwait(false);
            if (posted is not [.., var (login, text)] || _session.State.Login is not { } me || !string.Equals(login, me, StringComparison.OrdinalIgnoreCase) || !StewardActions.SameText(text, body))
            {
                var comments = $"{GitHubApi.Root}/repos/{pullRequest.Repository}/issues/{pullRequest.Number.ToString(CultureInfo.InvariantCulture)}/comments";
                GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Post, comments, new Dictionary<string, object> { ["body"] = body }, cancellationToken).ConfigureAwait(false));
            }

            GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Patch, pulls, new Dictionary<string, object> { ["state"] = "closed" }, cancellationToken).ConfigureAwait(false));
        }).ConfigureAwait(false);
    }

    private static string PullsUrlOf(PullRequestReview pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return PullsUrlOf(pullRequest.Repository, pullRequest.Number);
    }

    /// <summary>The pull request as GitHub has it now, when it is still open at the head commit and on the base branch that the review showed.</summary>
    /// <exception cref="PullRequestRefusedException">It is closed or merged, or it has another head commit or base branch.</exception>
    private async Task<PullDto> EnsureUnchangedAsync(string pulls, PullRequestReview pullRequest, CancellationToken cancellationToken)
    {
        var pull = await _api.GetAsync<PullDto>(pulls, cancellationToken).ConfigureAwait(false);
        if (pull.State != "open" || pull.Merged || pull.MergedAt is not null)
            throw new PullRequestRefusedException(PullRequestRefusal.NotOpen);

        // a pull request moved to another base keeps its head commit, and the merge confirmation names the base that the review showed
        if (!string.Equals(pull.Head?.Sha, pullRequest.HeadCommit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pull.Base?.Ref, pullRequest.BaseBranch, StringComparison.Ordinal))
        {
            throw new PullRequestRefusedException(PullRequestRefusal.Changed);
        }

        return pull;
    }

    /// <summary>The reviewDecision of GitHub, such as REVIEW_REQUIRED, or null when the rules require no review. REST has no such field.</summary>
    private async Task<string?> ReviewDecisionAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var slash = repository.IndexOf('/', StringComparison.Ordinal);
        var variables = new Dictionary<string, object> { ["owner"] = repository[..slash], ["name"] = repository[(slash + 1)..], ["number"] = number };
        var data = await _api.QueryAsync<DecisionDataDto>(ReviewDecisionQuery, variables, cancellationToken).ConfigureAwait(false);
        return data.Repository?.PullRequest is { } pull ? pull.ReviewDecision : throw new GitHubApiException(GitHubApiFailure.NotFound);
    }

    private sealed class MergeDto
    {
        public bool Merged { get; set; }
    }

    private sealed class DecisionDataDto
    {
        public DecisionRepositoryDto? Repository { get; set; }
    }

    private sealed class DecisionRepositoryDto
    {
        public DecisionPullDto? PullRequest { get; set; }
    }

    private sealed class DecisionPullDto
    {
        public string? ReviewDecision { get; set; }
    }
}
