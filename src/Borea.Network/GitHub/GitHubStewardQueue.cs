using System.Globalization;
using Borea.Core.GitHub;
using Borea.Core.Stewardship;

namespace Borea.Network.GitHub;

/// <summary>
/// IStewardQueue on the REST API with the token of the signed-in session. It reads no commit status, because the App has no
/// Commit statuses permission, so the verdict comment of the checks is what the queue shows.
/// </summary>
public sealed class GitHubStewardQueue : IStewardQueue
{
    /// <summary>How many pull requests of one repository are read at once, each with its files and comments.</summary>
    private const int ParallelReads = 4;

    private readonly IGitHubSession _session;
    private readonly GitHubApi _api;

    public GitHubStewardQueue(IGitHubSession session, HttpClient http, TimeProvider? time = null)
    {
        _session = session;
        _api = new GitHubApi(session, http, time);
    }

    public async Task<StewardQueue> ListAsync(StewardQueueFilter filter = StewardQueueFilter.NeedsSteward, CancellationToken cancellationToken = default)
    {
        if (_session.State.Status != GitHubSessionStatus.SignedIn)
            throw new StewardException(StewardFailure.SignedOut);

        var reads = StewardQueue.Repositories.Select(repository => ReadAsync(repository, filter, cancellationToken)).ToList();
        try
        {
            await Task.WhenAll(reads).ConfigureAwait(false);
        }
        catch (GitHubApiException exception)
        {
            throw exception.ToStewardException();
        }

        var items = reads
            .SelectMany(read => read.Result.Items)
            .OrderBy(item => item.Opened)
            .ThenBy(item => item.Repository, StringComparer.Ordinal)
            .ThenBy(item => item.Number)
            .ToList();
        return new StewardQueue(items, reads.Select(read => read.Result.Failure).OfType<StewardQueueFailure>().ToList());
    }

    /// <summary>The items of one repository, or why it could not be read. Only a sign-out ends the whole queue.</summary>
    private async Task<(IReadOnlyList<StewardQueueItem> Items, StewardQueueFailure? Failure)> ReadAsync(string repository, StewardQueueFilter filter, CancellationToken cancellationToken)
    {
        try
        {
            var pulls = new List<PullDto>();
            await foreach (var pull in _api.GetPagesAsync<PullDto>($"{GitHubApi.Root}/repos/{repository}/pulls?state=open&sort=created&direction=asc", cancellationToken).ConfigureAwait(false))
            {
                if (filter == StewardQueueFilter.AllOpen || NeedsSteward(pull))
                    pulls.Add(pull);
            }

            using var gate = new SemaphoreSlim(ParallelReads);
            var items = await Task.WhenAll(pulls.Select(async pull =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await ItemAsync(repository, pull, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);
            return (items, null);
        }
        catch (GitHubApiException exception) when (exception.Failure != GitHubApiFailure.SignedOut)
        {
            return ([], new StewardQueueFailure(repository, exception.ToStewardException()));
        }
    }

    private async Task<StewardQueueItem> ItemAsync(string repository, PullDto pull, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(pull.HtmlUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || pull.Number <= 0)
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);

        var number = pull.Number.ToString(CultureInfo.InvariantCulture);
        var files = new List<StewardQueueFile>();
        await foreach (var file in _api.GetPagesAsync<PullFileDto>($"{GitHubApi.Root}/repos/{repository}/pulls/{number}/files", cancellationToken).ConfigureAwait(false))
            files.Add(new StewardQueueFile(file.Filename, file.Status, file.PreviousFilename));

        var comments = new List<(string?, string?)>();
        await foreach (var comment in _api.GetPagesAsync<CommentDto>($"{GitHubApi.Root}/repos/{repository}/issues/{number}/comments", cancellationToken).ConfigureAwait(false))
            comments.Add((comment.User?.Login, comment.Body));

        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(repository, pull.Labels.Select(label => label.Name), files);
        return new StewardQueueItem(
            repository,
            pull.Number,
            url,
            pull.Title,
            pull.User?.Login,
            pull.CreatedAt,
            pull.Draft,
            NeedsSteward(pull),
            kinds,
            hasOtherFiles,
            IndexVerdict.Find(repository, comments));
    }

    private static bool NeedsSteward(PullDto pull) => pull.Labels.Any(label => label.Name == StewardQueueKinds.NeedsStewardLabel);

    private sealed class PullDto
    {
        public int Number { get; set; }

        public string Title { get; set; } = string.Empty;

        public string HtmlUrl { get; set; } = string.Empty;

        public bool Draft { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public UserDto? User { get; set; }

        public List<LabelDto> Labels { get; set; } = [];
    }

    private sealed class PullFileDto
    {
        public string Filename { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? PreviousFilename { get; set; }
    }

    private sealed class CommentDto
    {
        public string? Body { get; set; }

        public UserDto? User { get; set; }
    }

    private sealed class UserDto
    {
        public string? Login { get; set; }
    }

    private sealed class LabelDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
