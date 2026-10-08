using System.Net;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Stewardship;
using Borea.Network.Listings;

namespace Borea.Network.GitHub;

/// <summary>
/// IReleaseAmendments on the signed-in session of a steward. Every read takes one commit of the base branch, so the release files
/// and the game release list belong together. An amendment goes to a steward branch in content-index-releases itself, not to a fork.
/// </summary>
public sealed class GitHubReleaseAmendments : IReleaseAmendments
{
    private const string Api = GitHubApi.Root;

    private const string Repository = StewardAccess.ReleasesRepository;

    private const string GameVersionsPath = "game-versions.json";

    private const string ReleaseSuffix = ".json";

    private readonly IGitHubSession _session;
    private readonly IStewardRole _role;
    private readonly GitHubApi _api;
    private readonly IListingFormat _format;
    private readonly ListingOwnershipCheck _ownership;
    private readonly TimeProvider _time;
    private readonly string _base;

    /// <param name="http">Reads the release hosts that name the owner of a listing.</param>
    /// <param name="format">Reads the listing documents of content-index.</param>
    /// <param name="baseBranch">The branch of content-index-releases that the amendments start from and go to. Null takes main.</param>
    public GitHubReleaseAmendments(IGitHubSession session, IStewardRole role, HttpClient http, IListingFormat format, TimeProvider? time = null, string? baseBranch = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _role = role ?? throw new ArgumentNullException(nameof(role));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _time = time ?? TimeProvider.System;
        _base = baseBranch ?? ListingPullRequestLinks.DefaultBranch;
        _api = new GitHubApi(session, http, time, _base);
        _ownership = new ListingOwnershipCheck(_api, http, format);
    }

    public Task<IReadOnlyList<string>> ReleasesAsync(string listingId, CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        CheckId(listingId);
        await StewardAsync(cancellationToken).ConfigureAwait(false);
        var head = await HeadAsync(cancellationToken).ConfigureAwait(false);
        return ReleaseAmendment.Select(await StampedAsync(listingId, head, cancellationToken).ConfigureAwait(false), ReleaseSelection.All);
    });

    public Task<ReleaseAmendmentPreview> PreviewAsync(ReleaseAmendmentRequest request, CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(request);
        var login = await StewardAsync(cancellationToken).ConfigureAwait(false);
        return (await DeriveAsync(request, login, cancellationToken).ConfigureAwait(false)).Preview;
    });

    public Task<ReleaseAmendmentPullRequest> OpenAsync(ReleaseAmendmentPreview preview, CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(preview);
        var login = await StewardAsync(cancellationToken).ConfigureAwait(false);
        var (head, current) = await DeriveAsync(preview.Request, login, cancellationToken).ConfigureAwait(false);
        if (!current.HasSameFiles(preview))
            throw new ReleaseAmendmentChangedException(current);
        if (current.Changed.Count == 0)
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.InvalidChange, "every selected release already says this, so nothing is written");

        // The commit sits on the head that was read, so a file that changes on the base branch after this read conflicts on GitHub instead of being overwritten.
        var files = current.Changed.Select(file => (file.Path, file.After!)).ToList();
        var commit = await _api.CommitAsync(Repository, head, files, current.Title, cancellationToken).ConfigureAwait(false);
        var branch = await _api.CreateFreeBranchAsync(Repository, current.Request.Branch, commit, cancellationToken).ConfigureAwait(false);
        var request = new Dictionary<string, object>
        {
            ["title"] = current.Title,
            ["head"] = branch,
            ["base"] = _base,
            ["body"] = current.Body,
        };
        var pull = GitHubApi.Parse<PullDto>(GitHubApi.Ensure(await _api.SendAsync(HttpMethod.Post, $"{Api}/repos/{Repository}/pulls", request, cancellationToken).ConfigureAwait(false)));
        return Uri.TryCreate(pull.HtmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && pull.Number > 0
            ? new ReleaseAmendmentPullRequest(pull.Number, url, current.Title)
            : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
    });

    /// <summary>The login, when the role lets it bypass the ruleset of content-index-releases. GitHub would take the commits of any member, so Borea asks first.</summary>
    private async Task<string> StewardAsync(CancellationToken cancellationToken)
    {
        var login = _session.State is { Status: GitHubSessionStatus.SignedIn, Login: { } signedIn }
            ? signedIn
            : throw new StewardException(StewardFailure.SignedOut);
        var access = _role.Current ?? await _role.CheckAsync(cancellationToken).ConfigureAwait(false);
        return access is { ContentIndexReleases: true } && string.Equals(access.Login, login, StringComparison.OrdinalIgnoreCase)
            ? login
            : throw new StewardException(StewardFailure.NotSteward);
    }

    /// <summary>The amendment at the tip of the base branch, and that tip.</summary>
    private async Task<(string Head, ReleaseAmendmentPreview Preview)> DeriveAsync(ReleaseAmendmentRequest request, string login, CancellationToken cancellationToken)
    {
        CheckId(request.ListingId);
        if (!IndexStatusChange.IsValidReason(request.Reason))
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.InvalidChange, "the reason is one sentence on one line");
        if (request.AuthorRequest is { } link && !ReleaseAmendmentRequest.IsValidAuthorRequest(link))
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.InvalidChange, $"the author's request '{link}' is not one https link");

        var head = await HeadAsync(cancellationToken).ConfigureAwait(false);
        var amendment = ReleaseAmendment.Create(request.Amendment, await GameVersionsAsync(head, cancellationToken).ConfigureAwait(false), _time.GetUtcNow());
        var selected = ReleaseAmendment.Select(await StampedAsync(request.ListingId, head, cancellationToken).ConfigureAwait(false), request.Selection);

        var files = new List<ReleaseFilePreview>();
        foreach (var version in selected)
        {
            var path = ReleaseAmendment.PathOf(request.ListingId, version);
            var file = await _api.ReadFileAsync(Repository, path, head, cancellationToken).ConfigureAwait(false)
                ?? throw new GitHubApiException(GitHubApiFailure.NotFound, path);
            var text = file.Text ?? throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.NotStamperFile, $"{path} is no UTF-8 text");
            files.Add(new ReleaseFilePreview(version, path, text, amendment.Apply(path, text, request.Amender)?.Text));
        }

        var owners = await OwnersAsync(request.ListingId, login, cancellationToken).ConfigureAwait(false);
        return (head, new ReleaseAmendmentPreview(request, files, owners));
    }

    /// <summary>The id names a folder in the requests, so it must be a content id.</summary>
    private static void CheckId(string listingId)
    {
        if (!ModIds.IsValid(listingId))
            throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.UnknownRelease, $"'{listingId}' is no content id");
    }

    private async Task<string> HeadAsync(CancellationToken cancellationToken)
    {
        var reference = await _api.GetAsync<RefDto>($"{Api}/repos/{Repository}/git/ref/heads/{_base}", cancellationToken).ConfigureAwait(false);
        return reference.Object?.Sha is { Length: > 0 } sha ? sha : throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
    }

    /// <summary>The versions of game-versions.json at the commit.</summary>
    private async Task<IReadOnlyList<string>> GameVersionsAsync(string head, CancellationToken cancellationToken)
    {
        var file = await _api.ReadFileAsync(Repository, GameVersionsPath, head, cancellationToken).ConfigureAwait(false)
            ?? throw new GitHubApiException(GitHubApiFailure.NotFound, GameVersionsPath);
        try
        {
            return JsonSerializer.Deserialize<GameVersionsDto>(file.Text ?? string.Empty, GitHubApi.Json)?.Versions
                ?? throw new StewardException(StewardFailure.UnreadableFile, $"{GameVersionsPath} has no versions");
        }
        catch (JsonException exception)
        {
            throw new StewardException(StewardFailure.UnreadableFile, $"{GameVersionsPath} is not readable JSON", innerException: exception);
        }
    }

    /// <summary>The versions of the release files of the listing at the commit, as their file names spell them, as tools/amend.py finds them.</summary>
    private async Task<IReadOnlyList<string>> StampedAsync(string listingId, string head, CancellationToken cancellationToken)
    {
        var folder = $"{Api}/repos/{Repository}/contents/{ReleaseAmendment.Folder}/{Uri.EscapeDataString(listingId)}?ref={Uri.EscapeDataString(head)}";
        var reply = await _api.SendAsync(HttpMethod.Get, folder, null, cancellationToken).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return [];

        var body = GitHubApi.Ensure(reply);
        if (!body.TrimStart().StartsWith('['))
            return [];

        return [.. GitHubApi.Parse<List<FolderItemDto>>(body)
            .Where(item => item.Type == "file" && item.Name.EndsWith(ReleaseSuffix, StringComparison.Ordinal) && item.Name.Length > ReleaseSuffix.Length)
            .Select(item => item.Name[..^ReleaseSuffix.Length])];
    }

    /// <summary>The owners of the listing on the base branch of content-index, without the steward. An owner that cannot be read names nobody.</summary>
    private async Task<IReadOnlyList<string>> OwnersAsync(string listingId, string login, CancellationToken cancellationToken)
    {
        try
        {
            var path = $"{ListingDraft.ListingsFolder}/{listingId}.toml";
            if (await _api.ReadFileAsync(ListingPullRequestLinks.Repository, path, ListingPullRequestLinks.Branch, cancellationToken).ConfigureAwait(false) is not { Text: { } text })
                return [];

            var owners = await _ownership.OwnersAsync(ListingDraft.FromDocument(_format.Read(text)), cancellationToken).ConfigureAwait(false);
            return [.. owners.Where(owner => !string.Equals(owner, login, StringComparison.OrdinalIgnoreCase))];
        }
        catch (GitHubApiException exception) when (exception.Failure != GitHubApiFailure.SignedOut)
        {
            return [];
        }
        catch (FormatException)
        {
            return [];
        }
    }

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

    private sealed class RefDto
    {
        public ShaDto? Object { get; set; }
    }

    private sealed class ShaDto
    {
        public string? Sha { get; set; }
    }

    private sealed class FolderItemDto
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class PullDto
    {
        public int Number { get; set; }

        public string HtmlUrl { get; set; } = string.Empty;
    }

    private sealed class GameVersionsDto
    {
        public List<string>? Versions { get; set; }
    }
}
