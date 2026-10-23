using System.Globalization;
using System.Text;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using Borea.Network.Listings;

namespace Borea.Network.GitHub;

/// <summary>
/// IIndexStatusEditor on the signed-in session of a steward. Every read takes one commit of the base branch, so the file,
/// the ids it may name and the owners belong together. A change goes to a steward branch in content-index itself, not to a fork.
/// </summary>
public sealed class GitHubIndexStatusEditor : IIndexStatusEditor
{
    private const string Api = GitHubApi.Root;

    private const string Repository = ListingPullRequestLinks.Repository;

    private const string PackOwnerFile = "owner.json";

    private readonly IGitHubSession _session;
    private readonly IStewardRole _role;
    private readonly GitHubApi _api;
    private readonly IListingFormat _format;
    private readonly ListingOwnershipCheck _ownership;
    private readonly TimeProvider _time;
    private readonly string _base;

    /// <param name="http">Reads the release hosts that name the owner of a listing.</param>
    /// <param name="baseBranch">The branch of content-index that the changes start from and go to. Null takes <see cref="ListingPullRequestLinks.Branch"/>.</param>
    public GitHubIndexStatusEditor(IGitHubSession session, IStewardRole role, HttpClient http, IListingFormat format, TimeProvider? time = null, string? baseBranch = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _role = role ?? throw new ArgumentNullException(nameof(role));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _time = time ?? TimeProvider.System;
        _base = baseBranch ?? ListingPullRequestLinks.Branch;
        _api = new GitHubApi(session, http, time, _base);
        _ownership = new ListingOwnershipCheck(_api, http, format);
    }

    public Task<IndexStatusOverview> ReadAsync(CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        Login();
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        var open = await OpenPullRequestsAsync(withConflicts: true, cancellationToken).ConfigureAwait(false);
        return new IndexStatusOverview(commit.Document.Entries, open);
    });

    public Task<IndexStatusCheck> CheckAsync(IndexStatusChange change, CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(change);
        var login = await StewardAsync(cancellationToken).ConfigureAwait(false);
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        IndexStatusRefusal? refusal = null;
        try
        {
            commit.Document.Check(change, commit.Contents);
        }
        catch (IndexStatusRefusedException exception)
        {
            refusal = exception.Refusal;
        }

        IReadOnlyList<IndexStatusPullRequest>? open;
        try
        {
            open = await OpenPullRequestsAsync(withConflicts: false, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubApiException exception) when (exception.Failure != GitHubApiFailure.SignedOut)
        {
            open = null;
        }

        var (owners, isOwner) = await OwnersAsync(change.Id, commit, login, cancellationToken).ConfigureAwait(false);
        return new IndexStatusCheck(refusal, open, owners, isOwner);
    });

    public Task<IndexStatusPullRequest> OpenAsync(IndexStatusChange change, CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(change);
        var login = await StewardAsync(cancellationToken).ConfigureAwait(false);
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        var changed = commit.Document.Apply(change, commit.Contents, _time.GetUtcNow());
        var id = commit.Contents.ListingId(change.Id) ?? commit.Contents.PackId(change.Id) ?? change.Id;
        change = change with { Id = id, Version = change.Version is { } version ? commit.Contents.PackVersion(id, version) ?? version : null };
        var (owners, _) = await OwnersAsync(change.Id, commit, login, cancellationToken).ConfigureAwait(false);

        var branch = await _api.CreateFreeBranchAsync(Repository, change.Branch, commit.Head, cancellationToken).ConfigureAwait(false);
        var put = new Dictionary<string, object>
        {
            ["message"] = change.Title,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(changed.Text)),
            ["branch"] = branch,
            ["sha"] = commit.FileSha,
        };
        GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Put, $"{Api}/repos/{Repository}/contents/{IndexStatusDocument.Path}", put, cancellationToken).ConfigureAwait(false));

        var request = new Dictionary<string, object>
        {
            ["title"] = change.Title,
            ["head"] = branch,
            ["base"] = _base,
            ["body"] = change.Body(owners),
        };
        var reply = await _api.SendAsync(HttpMethod.Post, $"{Api}/repos/{Repository}/pulls", request, cancellationToken).ConfigureAwait(false);
        var pull = GitHubApi.Parse<PullDto>(GitHubApi.Ensure(reply));
        return PullRequestOf(pull, login);
    });

    public Task<IReadOnlyList<string>> OwnedAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => GuardAsync<IReadOnlyList<string>>(async () =>
    {
        ArgumentNullException.ThrowIfNull(ids);
        var login = Login();
        var owned = new List<string>();
        if (ids.Count == 0)
            return owned;

        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if ((await OwnersAsync(id, commit, login, cancellationToken).ConfigureAwait(false)).IsOwner)
                owned.Add(id);
        }

        return owned;
    });

    private string Login() =>
        _session.State is { Status: GitHubSessionStatus.SignedIn, Login: { } login }
            ? login
            : throw new StewardException(StewardFailure.SignedOut);

    /// <summary>The login, when the role lets it bypass the ruleset of content-index. GitHub would take the commits of any member, so Borea asks first.</summary>
    private async Task<string> StewardAsync(CancellationToken cancellationToken)
    {
        var login = Login();
        var access = _role.Current ?? await _role.CheckAsync(cancellationToken).ConfigureAwait(false);
        return access is { ContentIndex: true } && string.Equals(access.Login, login, StringComparison.OrdinalIgnoreCase)
            ? login
            : throw new StewardException(StewardFailure.NotSteward);
    }

    /// <summary>The tip of the base branch, its index-status.toml and the ids of its documents.</summary>
    private async Task<BaseCommit> ReadBaseAsync(CancellationToken cancellationToken)
    {
        var reference = await _api.GetAsync<RefDto>($"{Api}/repos/{Repository}/git/ref/heads/{_base}", cancellationToken).ConfigureAwait(false);
        var head = reference.Object?.Sha is { Length: > 0 } sha ? sha : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);

        var tree = await _api.GetAsync<TreeDto>($"{Api}/repos/{Repository}/git/trees/{head}?recursive=1", cancellationToken).ConfigureAwait(false);
        if (tree.Truncated)
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, "the file tree of content-index is too large to read at once");

        var file = await _api.ReadFileAsync(Repository, IndexStatusDocument.Path, head, cancellationToken).ConfigureAwait(false)
            ?? throw new GitHubApiException(GitHubApiFailure.NotFound, IndexStatusDocument.Path);
        IndexStatusDocument document;
        try
        {
            document = IndexStatusDocument.Parse(file.Text ?? throw new FormatException($"{IndexStatusDocument.Path} is no UTF-8 text."));
        }
        catch (FormatException exception)
        {
            throw new StewardException(StewardFailure.UnreadableFile, exception.Message, innerException: exception);
        }

        var contents = IndexContents.FromPaths(tree.Tree.Where(item => item.Type == "blob").Select(item => item.Path));
        return new BaseCommit(head, file.Sha, document, contents);
    }

    /// <summary>The open pull requests to the base branch that change the file, which conflict with each other once one of them merges.</summary>
    /// <param name="withConflicts">Also asks GitHub whether each one still merges, which takes one more request per pull request.</param>
    private async Task<IReadOnlyList<IndexStatusPullRequest>> OpenPullRequestsAsync(bool withConflicts, CancellationToken cancellationToken)
    {
        var found = new List<IndexStatusPullRequest>();
        await foreach (var pull in _api.GetPagesAsync<PullDto>($"{Api}/repos/{Repository}/pulls?state=open&base={Uri.EscapeDataString(_base)}", cancellationToken).ConfigureAwait(false))
        {
            var changesFile = false;
            await foreach (var file in _api.GetPagesAsync<PullFileDto>($"{Api}/repos/{Repository}/pulls/{pull.Number.ToString(CultureInfo.InvariantCulture)}/files", cancellationToken).ConfigureAwait(false))
            {
                if (file.Filename == IndexStatusDocument.Path || file.PreviousFilename == IndexStatusDocument.Path)
                {
                    changesFile = true;
                    break;
                }
            }

            if (!changesFile)
                continue;

            var listed = PullRequestOf(pull, pull.User?.Login);
            if (withConflicts)
            {
                var single = await _api.GetAsync<PullDto>($"{Api}/repos/{Repository}/pulls/{pull.Number.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
                listed = listed with { Conflicts = single.Mergeable is { } mergeable ? !mergeable : null };
            }

            found.Add(listed);
        }

        return found.OrderBy(pull => pull.Number).ToList();
    }

    /// <summary>
    /// The owners of the listing or pack at the base commit, without the steward, and whether the steward is one of them.
    /// A listing's owner is who its host proves, and a pack's is its accepted owner record. An owner that cannot be read names nobody.
    /// </summary>
    private async Task<(IReadOnlyList<string> Owners, bool IsOwner)> OwnersAsync(string id, BaseCommit commit, string login, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> owners = [];
        var isOwner = false;
        try
        {
            if (commit.Contents.ListingId(id) is { } listing)
            {
                var path = $"{ListingDraft.ListingsFolder}/{listing}.toml";
                if (await _api.ReadFileAsync(Repository, path, commit.Head, cancellationToken).ConfigureAwait(false) is { Text: { } text })
                    owners = await _ownership.OwnersAsync(ListingDraft.FromDocument(_format.Read(text)), cancellationToken).ConfigureAwait(false);

                isOwner = owners.Contains(login, StringComparer.OrdinalIgnoreCase);
            }
            else if (commit.Contents.PackId(id) is { } pack)
            {
                var path = $"{IndexContents.PacksFolder}/{pack}/{PackOwnerFile}";
                if (await _api.ReadFileAsync(Repository, path, commit.Head, cancellationToken).ConfigureAwait(false) is { Text: { } text }
                    && GitHubApi.Parse<PackOwnerDto>(text) is { GithubLogin: { Length: > 0 } owner } record)
                {
                    // The record keeps the login of its claim, which can be stale, so the account id decides.
                    var user = await _api.GetAsync<UserDto>($"{Api}/user", cancellationToken).ConfigureAwait(false);
                    isOwner = record.GithubId is { } ownerId ? ownerId == user.Id : string.Equals(owner, login, StringComparison.OrdinalIgnoreCase);
                    owners = isOwner ? [] : [owner];
                }
            }
        }
        catch (GitHubApiException exception) when (exception.Failure != GitHubApiFailure.SignedOut)
        {
            owners = [];
        }
        catch (FormatException)
        {
            owners = [];
        }

        return (owners.Where(owner => !string.Equals(owner, login, StringComparison.OrdinalIgnoreCase)).ToList(), isOwner);
    }

    private static IndexStatusPullRequest PullRequestOf(PullDto pull, string? author) =>
        Uri.TryCreate(pull.HtmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && pull.Number > 0
            ? new IndexStatusPullRequest(pull.Number, url, pull.Title, author)
            : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);

    private static async Task<T> GuardAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (GitHubApiException exception)
        {
            throw exception.ToStewardException();
        }
    }

    /// <param name="Head">The commit of the base branch that every read took.</param>
    /// <param name="FileSha">The blob of index-status.toml, which the commit replaces.</param>
    private sealed record BaseCommit(string Head, string FileSha, IndexStatusDocument Document, IndexContents Contents);

    private sealed class RefDto
    {
        public ShaDto? Object { get; set; }
    }

    private sealed class ShaDto
    {
        public string? Sha { get; set; }
    }

    private sealed class TreeDto
    {
        public List<TreeItemDto> Tree { get; set; } = [];

        public bool Truncated { get; set; }
    }

    private sealed class TreeItemDto
    {
        public string Path { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class PullDto
    {
        public int Number { get; set; }

        public string Title { get; set; } = string.Empty;

        public string HtmlUrl { get; set; } = string.Empty;

        public bool? Mergeable { get; set; }

        public UserDto? User { get; set; }
    }

    private sealed class PullFileDto
    {
        public string Filename { get; set; } = string.Empty;

        public string? PreviousFilename { get; set; }
    }

    private sealed class UserDto
    {
        public long Id { get; set; }

        public string? Login { get; set; }
    }

    /// <summary>packs/&lt;id&gt;/owner.json of content-index.</summary>
    private sealed class PackOwnerDto
    {
        public string? GithubLogin { get; set; }

        public long? GithubId { get; set; }
    }
}
