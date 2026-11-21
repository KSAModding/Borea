using System.Globalization;
using System.Text;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Mods;
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

    public Task<IndexStatusOverview> ReadAsync(CancellationToken cancellationToken = default) => StewardActions.GuardAsync(async () =>
    {
        Login();
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        var open = await OpenPullRequestsAsync(withConflicts: true, cancellationToken).ConfigureAwait(false);
        return new IndexStatusOverview(commit.Document.Entries, open);
    });

    public Task<IndexStatusCheck> CheckAsync(IndexStatusChange change, CancellationToken cancellationToken = default) => StewardActions.GuardAsync(async () =>
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
        IndexStatusPullRequest? reuses = null;
        try
        {
            var pulls = await StatusPullsAsync(cancellationToken).ConfigureAwait(false);
            open = pulls.Select(status => PullRequestOf(status.Pull, status.Pull.User?.Login)).ToList();
            if (await OwnPullAsync(pulls, login, cancellationToken).ConfigureAwait(false) is { } own)
            {
                reuses = PullRequestOf(own.Pull, login);
                if (refusal is null)
                {
                    try
                    {
                        (await ReadStatusAsync(own.Head, cancellationToken).ConfigureAwait(false)).Document.Check(change, commit.Contents);
                    }
                    catch (IndexStatusRefusedException exception)
                    {
                        refusal = exception.Refusal;
                    }
                }
            }
        }
        catch (GitHubApiException exception) when (exception.Failure != GitHubApiFailure.SignedOut)
        {
            open = null;
            reuses = null;
        }

        var (owners, isOwner) = await OwnersAsync(change.Id, commit, login, cancellationToken).ConfigureAwait(false);
        return new IndexStatusCheck(refusal, open, owners, isOwner) { Reuses = reuses };
    });

    public Task<IndexStatusPullRequest> OpenAsync(IndexStatusChange change, CancellationToken cancellationToken = default) => StewardActions.GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(change);
        var login = await StewardAsync(cancellationToken).ConfigureAwait(false);
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        var changed = commit.Document.Apply(change, commit.Contents, _time.GetUtcNow());
        var id = commit.Contents.ListingId(change.Id) ?? commit.Contents.PackId(change.Id) ?? change.Id;
        change = change with { Id = id, Version = change.Version is { } version ? commit.Contents.PackVersion(id, version) ?? version : null };
        var (owners, _) = await OwnersAsync(change.Id, commit, login, cancellationToken).ConfigureAwait(false);
        if (await OwnPullAsync(await StatusPullsAsync(cancellationToken).ConfigureAwait(false), login, cancellationToken).ConfigureAwait(false) is { } own)
            return await AddToPullRequestAsync(own, commit, change, change.Body(owners), login, cancellationToken).ConfigureAwait(false);

        return await OpenPullRequestAsync(commit, changed, change.Branch, change.Title, change.Body(owners), login, cancellationToken).ConfigureAwait(false);
    });

    public Task<IReadOnlyList<string>> PackMembersAsync(string packId, CancellationToken cancellationToken = default) => StewardActions.GuardAsync<IReadOnlyList<string>>(async () =>
    {
        ArgumentNullException.ThrowIfNull(packId);
        Login();
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        var versions = await PackPinsAsync(packId, commit, cancellationToken).ConfigureAwait(false);
        return versions.SelectMany(version => version.Mods).Distinct(ModIds.Comparer).Order(StringComparer.OrdinalIgnoreCase).ToList();
    });

    public Task<PackLeaveCheck> CheckLeaveAsync(PackLeave leave, CancellationToken cancellationToken = default) => StewardActions.GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(leave);
        var login = await StewardAsync(cancellationToken).ConfigureAwait(false);
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        leave = SpelledAsListed(leave, commit);
        var (plan, _) = await PlanAsync(leave, commit, cancellationToken).ConfigureAwait(false);
        var ownership = await ReporterOwnershipAsync(leave, commit, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<IndexStatusPullRequest>? open;
        try
        {
            open = await OpenPullRequestsAsync(withConflicts: false, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubApiException exception) when (exception.Failure != GitHubApiFailure.SignedOut)
        {
            open = null;
        }

        var (owners, isOwner) = await OwnersAsync(leave.PackId, commit, login, cancellationToken).ConfigureAwait(false);
        return new PackLeaveCheck(plan.Refusal, plan.Versions, plan.Retracted, ownership, open, owners, isOwner);
    });

    public Task<IndexStatusPullRequest> OpenLeaveAsync(PackLeave leave, CancellationToken cancellationToken = default) => StewardActions.GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(leave);
        var login = await StewardAsync(cancellationToken).ConfigureAwait(false);
        var commit = await ReadBaseAsync(cancellationToken).ConfigureAwait(false);
        leave = SpelledAsListed(leave, commit);
        var (plan, changed) = await PlanAsync(leave, commit, cancellationToken).ConfigureAwait(false);
        if (changed is null)
            throw new IndexStatusRefusedException(plan.Refusal ?? IndexStatusRefusal.NotPinned, leave.PackId);

        var ownership = await ReporterOwnershipAsync(leave, commit, cancellationToken).ConfigureAwait(false);
        var proven = ownership.State == ListingOwnershipState.Verified;
        if (!proven && !leave.ConfirmedByForums)
            throw new IndexStatusRefusedException(IndexStatusRefusal.NotConfirmed, leave.PackId);

        var (owners, _) = await OwnersAsync(leave.PackId, commit, login, cancellationToken).ConfigureAwait(false);
        return await OpenPullRequestAsync(commit, changed, leave.Branch, leave.Title, leave.Body(plan.Versions, proven, owners), login, cancellationToken).ConfigureAwait(false);
    });

    public Task<IReadOnlyList<string>> OwnedAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => StewardActions.GuardAsync<IReadOnlyList<string>>(async () =>
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

        var (fileSha, document) = await ReadStatusAsync(head, cancellationToken).ConfigureAwait(false);
        var contents = IndexContents.FromPaths(tree.Tree.Where(item => item.Type == "blob").Select(item => item.Path));
        return new BaseCommit(head, fileSha, document, contents);
    }

    /// <summary>index-status.toml at the commit <paramref name="sha"/>, with its blob sha.</summary>
    private async Task<(string FileSha, IndexStatusDocument Document)> ReadStatusAsync(string sha, CancellationToken cancellationToken)
    {
        var file = await _api.ReadFileAsync(Repository, IndexStatusDocument.Path, sha, cancellationToken).ConfigureAwait(false)
            ?? throw new GitHubApiException(GitHubApiFailure.NotFound, IndexStatusDocument.Path);
        try
        {
            return (file.Sha, IndexStatusDocument.Parse(file.Text ?? throw new FormatException($"{IndexStatusDocument.Path} is no UTF-8 text.")));
        }
        catch (FormatException exception)
        {
            throw new StewardException(StewardFailure.UnreadableFile, exception.Message, innerException: exception);
        }
    }

    /// <summary>The open pull requests to the base branch that change the file, which conflict with each other once one of them merges.</summary>
    /// <param name="withConflicts">Also asks GitHub whether each one still merges, which takes one more request per pull request.</param>
    private async Task<IReadOnlyList<IndexStatusPullRequest>> OpenPullRequestsAsync(bool withConflicts, CancellationToken cancellationToken)
    {
        var found = new List<IndexStatusPullRequest>();
        foreach (var (pull, _) in await StatusPullsAsync(cancellationToken).ConfigureAwait(false))
        {
            var listed = PullRequestOf(pull, pull.User?.Login);
            if (withConflicts)
            {
                var single = await _api.GetAsync<PullDto>($"{Api}/repos/{Repository}/pulls/{pull.Number.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
                listed = listed with { Conflicts = single.Mergeable is { } mergeable ? !mergeable : null };
            }

            found.Add(listed);
        }

        return found;
    }

    /// <summary>The open pull requests to the base branch that change the file, oldest first, each with whether it changes nothing else.</summary>
    private async Task<IReadOnlyList<(PullDto Pull, bool OnlyFile)>> StatusPullsAsync(CancellationToken cancellationToken)
    {
        var found = new List<(PullDto Pull, bool OnlyFile)>();
        await foreach (var pull in _api.GetPagesAsync<PullDto>($"{Api}/repos/{Repository}/pulls?state=open&base={Uri.EscapeDataString(_base)}", cancellationToken).ConfigureAwait(false))
        {
            var changesFile = false;
            var onlyFile = true;
            await foreach (var file in _api.GetPagesAsync<PullFileDto>($"{Api}/repos/{Repository}/pulls/{pull.Number.ToString(CultureInfo.InvariantCulture)}/files", cancellationToken).ConfigureAwait(false))
            {
                var isFile = file.Filename == IndexStatusDocument.Path && file.PreviousFilename is null or IndexStatusDocument.Path;
                changesFile |= isFile || file.PreviousFilename == IndexStatusDocument.Path;
                onlyFile &= isFile;
            }

            if (changesFile)
                found.Add((pull, onlyFile));
        }

        return found.OrderBy(status => status.Pull.Number).ToList();
    }

    /// <summary>
    /// The oldest of <paramref name="pulls"/> that a new change of the steward goes onto, because a second pull request would conflict with it:
    /// one that the steward opened from a steward branch of content-index, that changes only the file, and that GitHub can merge.
    /// A pull request whose mergeable state GitHub has not worked out yet does not count.
    /// </summary>
    private async Task<OwnPull?> OwnPullAsync(IReadOnlyList<(PullDto Pull, bool OnlyFile)> pulls, string login, CancellationToken cancellationToken)
    {
        foreach (var (pull, onlyFile) in pulls)
        {
            if (!onlyFile || !string.Equals(pull.User?.Login, login, StringComparison.OrdinalIgnoreCase))
                continue;

            var single = await _api.GetAsync<PullDto>($"{Api}/repos/{Repository}/pulls/{pull.Number.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
            if (single is { Mergeable: true, Head: { Ref: { } branch, Sha: { Length: > 0 } head, Repo.FullName: { } repository } }
                && string.Equals(repository, Repository, StringComparison.OrdinalIgnoreCase)
                && branch.StartsWith(IndexStatusChange.BranchPrefix, StringComparison.Ordinal))
                return new OwnPull(single, branch, head);
        }

        return null;
    }

    /// <summary>
    /// The owners of the listing, vehicle, save or pack at the base commit, without the steward, and whether the steward is one of them.
    /// A listing's owner is who its host proves, and a pack's is its accepted owner record. An owner that cannot be read names nobody.
    /// </summary>
    private async Task<(IReadOnlyList<string> Owners, bool IsOwner)> OwnersAsync(string id, BaseCommit commit, string login, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> owners = [];
        var isOwner = false;
        try
        {
            if (commit.Contents.ListingPath(id) is { } listing)
            {
                if (await _api.ReadFileAsync(Repository, listing, commit.Head, cancellationToken).ConfigureAwait(false) is { Text: { } text })
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

    /// <summary>The pack id as its folder spells it, and the mod id as its listing spells it, so the title and the reason name them so.</summary>
    private static PackLeave SpelledAsListed(PackLeave leave, BaseCommit commit) => leave with
    {
        PackId = commit.Contents.PackId(leave.PackId) ?? leave.PackId,
        ModId = commit.Contents.ListingId(leave.ModId) ?? leave.ModId,
    };

    /// <summary>Commits the changed file to a free steward branch cut from the base commit, and opens its pull request.</summary>
    private async Task<IndexStatusPullRequest> OpenPullRequestAsync(BaseCommit commit, IndexStatusDocument changed, string branchName, string title, string body, string login, CancellationToken cancellationToken)
    {
        var branch = await _api.CreateFreeBranchAsync(Repository, branchName, commit.Head, cancellationToken).ConfigureAwait(false);
        var put = new Dictionary<string, object>
        {
            ["message"] = title,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(changed.Text)),
            ["branch"] = branch,
            ["sha"] = commit.FileSha,
        };
        GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Put, $"{Api}/repos/{Repository}/contents/{IndexStatusDocument.Path}", put, cancellationToken).ConfigureAwait(false));

        var request = new Dictionary<string, object>
        {
            ["title"] = title,
            ["head"] = branch,
            ["base"] = _base,
            ["body"] = body,
        };
        var reply = await _api.SendAsync(HttpMethod.Post, $"{Api}/repos/{Repository}/pulls", request, cancellationToken).ConfigureAwait(false);
        var pull = GitHubApi.Parse<PullDto>(GitHubApi.Ensure(reply));
        return PullRequestOf(pull, login);
    }

    /// <summary>
    /// Applies the change to the file at the head of the open pull request with the same checks, commits it to that branch on top of
    /// its head, and gives the pull request a title that names every change since the branch left the base branch. The body keeps what
    /// it says, with the reason and owners of each earlier change, and adds <paramref name="body"/>.
    /// </summary>
    /// <exception cref="IndexStatusRefusedException">The checks of content-index would refuse the file on that branch, for example because it has the entry already. Nothing was written.</exception>
    private async Task<IndexStatusPullRequest> AddToPullRequestAsync(OwnPull own, BaseCommit commit, IndexStatusChange change, string body, string login, CancellationToken cancellationToken)
    {
        var (fileSha, document) = await ReadStatusAsync(own.Head, cancellationToken).ConfigureAwait(false);
        var changed = document.Apply(change, commit.Contents, _time.GetUtcNow());
        var compare = await _api.GetAsync<CompareDto>($"{Api}/repos/{Repository}/compare/{_base}...{own.Head}", cancellationToken).ConfigureAwait(false);
        var mergeBase = compare.MergeBaseCommit?.Sha is { Length: > 0 } sha ? sha : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
        var (_, before) = await ReadStatusAsync(mergeBase, cancellationToken).ConfigureAwait(false);

        // The blob sha of the head makes GitHub refuse the commit when the branch moved since it was read.
        var put = new Dictionary<string, object>
        {
            ["message"] = change.Title,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(changed.Text)),
            ["branch"] = own.Branch,
            ["sha"] = fileSha,
        };
        GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Put, $"{Api}/repos/{Repository}/contents/{IndexStatusDocument.Path}", put, cancellationToken).ConfigureAwait(false));

        var request = new Dictionary<string, object>
        {
            ["title"] = changed.PullRequestTitle(before),
            ["body"] = string.IsNullOrWhiteSpace(own.Pull.Body) ? body : own.Pull.Body.TrimEnd() + "\n\n" + body,
        };
        var number = own.Pull.Number.ToString(CultureInfo.InvariantCulture);
        var reply = await _api.SendAsync(HttpMethod.Patch, $"{Api}/repos/{Repository}/pulls/{number}", request, cancellationToken).ConfigureAwait(false);
        return PullRequestOf(GitHubApi.Parse<PullDto>(GitHubApi.Ensure(reply)), login) with { IsReused = true };
    }

    /// <summary>
    /// The versions that a leave retracts at the base commit, and the file with one retracted entry for each of them, checked as
    /// tools/check_status.py checks it. The file is null when the plan has a refusal.
    /// </summary>
    private async Task<(PackLeavePlan Plan, IndexStatusDocument? Changed)> PlanAsync(PackLeave leave, BaseCommit commit, CancellationToken cancellationToken)
    {
        if (commit.Contents.PackId(leave.PackId) is null)
            return (new PackLeavePlan([], [], IndexStatusRefusal.NotAPack), null);

        var plan = leave.Plan(await PackPinsAsync(leave.PackId, commit, cancellationToken).ConfigureAwait(false), commit.Document.Entries);
        if (plan.Refusal is not null)
            return (plan, null);

        try
        {
            var changed = commit.Document;
            foreach (var version in plan.Versions)
                changed = changed.Apply(leave.ChangeOf(version), commit.Contents, _time.GetUtcNow());

            return (plan, changed);
        }
        catch (IndexStatusRefusedException exception)
        {
            return (plan with { Refusal = exception.Refusal }, null);
        }
    }

    /// <summary>Each version of the pack at the base commit with the ids it pins. A pack that is not there has none.</summary>
    private async Task<IReadOnlyList<PackVersionPins>> PackPinsAsync(string packId, BaseCommit commit, CancellationToken cancellationToken)
    {
        if (commit.Contents.PackId(packId) is not { } pack)
            return [];

        var versions = new List<PackVersionPins>();
        foreach (var version in commit.Contents.PackVersions(pack))
        {
            var path = $"{IndexContents.PacksFolder}/{pack}/{version}.toml";
            var file = await _api.ReadFileAsync(Repository, path, commit.Head, cancellationToken).ConfigureAwait(false)
                ?? throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, $"{path} is not at {commit.Head}");
            ListingDraft draft;
            try
            {
                draft = ListingDraft.FromDocument(_format.Read(file.Text ?? throw new FormatException($"{path} is no UTF-8 text.")));
            }
            catch (FormatException exception)
            {
                throw new StewardException(StewardFailure.UnreadableFile, $"{path}: {exception.Message}", innerException: exception);
            }

            versions.Add(new PackVersionPins(version, draft.Mods.Select(member => member.Id).Where(id => id.Length > 0).ToList()));
        }

        return versions;
    }

    /// <summary>
    /// The ownership proof of the reporter for the listing of the mod at the base commit, as the checks of content-index read it, with the
    /// forums thread of the listing. A mod without a listing, or a report without an author, proves nothing.
    /// </summary>
    private async Task<ListingOwnership> ReporterOwnershipAsync(PackLeave leave, BaseCommit commit, CancellationToken cancellationToken)
    {
        if (commit.Contents.ListingId(leave.ModId) is not { } listing)
            return ListingOwnership.Unknown;

        var path = $"{ListingDraft.ListingsFolder}/{listing}.toml";
        if (await _api.ReadFileAsync(Repository, path, commit.Head, cancellationToken).ConfigureAwait(false) is not { Text: { } text })
            return ListingOwnership.Unknown;

        ListingDraft draft;
        try
        {
            draft = ListingDraft.FromDocument(_format.Read(text));
        }
        catch (FormatException)
        {
            return ListingOwnership.Unknown;
        }

        if (leave.Reporter is { } reporter)
            return await _ownership.CheckAsync(reporter, draft, null, cancellationToken).ConfigureAwait(false);

        return ListingOwnership.Unknown with { ForumsThread = ListingOwnershipCheck.ForumsThreadOf(draft) };
    }

    /// <param name="Head">The commit of the base branch that every read took.</param>
    /// <param name="FileSha">The blob of index-status.toml, which the commit replaces.</param>
    private sealed record BaseCommit(string Head, string FileSha, IndexStatusDocument Document, IndexContents Contents);

    /// <param name="Branch">The steward branch of content-index that the pull request merges.</param>
    /// <param name="Head">The commit at the head of that branch, which the next commit goes on top of.</param>
    private sealed record OwnPull(PullDto Pull, string Branch, string Head);

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

        public string? Body { get; set; }

        public bool? Mergeable { get; set; }

        public UserDto? User { get; set; }

        public HeadDto? Head { get; set; }
    }

    private sealed class HeadDto
    {
        public string? Ref { get; set; }

        public string? Sha { get; set; }

        public RepositoryDto? Repo { get; set; }
    }

    private sealed class RepositoryDto
    {
        public string? FullName { get; set; }
    }

    private sealed class CompareDto
    {
        public ShaDto? MergeBaseCommit { get; set; }
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
