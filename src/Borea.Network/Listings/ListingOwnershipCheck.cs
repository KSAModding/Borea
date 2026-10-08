using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Network.GitHub;
using Borea.Network.SpaceDock;

namespace Borea.Network.Listings;

/// <summary>
/// IListingOwnershipCheck after tools/ownership.py of content-index. The release hosts are read without the token, as the checks read them,
/// and the documents of content-index through the signed-in session.
/// </summary>
public sealed partial class ListingOwnershipCheck : IListingOwnershipCheck
{
    private const string Api = GitHubApi.Root;

    private const int SpaceDockGameId = 22409;

    private readonly HttpClient _http;
    private readonly IListingFormat _format;
    private readonly GitHubApi _api;

    /// <param name="http">Reads SpaceDock and the release repositories without the token.</param>
    /// <param name="format">Reads the marker file and the listing documents.</param>
    public ListingOwnershipCheck(IGitHubSession session, HttpClient http, IListingFormat format, TimeProvider? time = null)
        : this(new GitHubApi(session, http, time), http, format)
    {
    }

    internal ListingOwnershipCheck(GitHubApi api, HttpClient http, IListingFormat format)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _format = format ?? throw new ArgumentNullException(nameof(format));
    }

    public async Task<ListingOwnership> CheckAsync(GitHubAccount author, ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(submitted);
        ListingOwnership ownership;
        try
        {
            ownership = await VerifyChangeAsync(submitted, listed, author, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubApiException)
        {
            ownership = ListingOwnership.Unknown;
        }

        // An edit never shows the thread its author submits, so a steward does not contact the author in place of the owner.
        return ownership with { ForumsThread = ForumsThreadOf(listed ?? submitted) };
    }

    public async Task<ListingOwnership> CheckPullRequestAsync(GitHubAccount author, string path, string baseBranch, string headCommit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(headCommit);
        if (!IsListingPath(path))
            throw new ArgumentException($"{path} is no listing document.", nameof(path));

        try
        {
            if (await ReadListingAsync(path, headCommit, cancellationToken).ConfigureAwait(false) is not { } submitted)
                return ListingOwnership.Unknown;

            // The tip of the base branch and not the commit the pull request was cut from, so a stale pull request never verifies against a previous owner.
            var listed = await ReadListingAsync(path, baseBranch, cancellationToken).ConfigureAwait(false);
            return await CheckAsync(author, submitted, listed, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubApiException exception) when (exception.Failure == GitHubApiFailure.SignedOut)
        {
            throw new ListingPublishException(ListingPublishFailure.SignedOut, ListingPublishStep.Ownership, innerException: exception);
        }
        catch (GitHubApiException)
        {
            return ListingOwnership.Unknown;
        }
    }

    public async Task<IReadOnlyList<string>> OwnersAsync(ListingDraft listed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listed);
        try
        {
            var target = ListingAuthority.Of(listed) switch
            {
                { Kind: ListingAuthority.GitHub } github => github.Target,
                { Kind: ListingAuthority.SpaceDock } spaceDock => (await SpaceDockRepositoryAsync(spaceDock.Target, cancellationToken).ConfigureAwait(false)).Repository,
                _ => null,
            };
            return target is null ? [] : await RepositoryOwnersAsync(target, listed.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubApiException)
        {
            return [];
        }
    }

    /// <summary>The owners that owner_logins of tools/ownership.py names, sorted and without a login twice.</summary>
    private async Task<IReadOnlyList<string>> RepositoryOwnersAsync(string target, string id, CancellationToken cancellationToken)
    {
        var reply = await _api.SendAsync(HttpMethod.Get, $"{Api}/repos/{target}", null, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return [];

        var repository = GitHubApi.Parse<RepositoryDto>(GitHubApi.Ensure(reply));
        if (!string.Equals(repository.FullName, target, StringComparison.OrdinalIgnoreCase))
            return [];
        if (repository.Owner?.Type == "User")
            return repository.Owner.Login is { } owner && Login().IsMatch(owner) ? [owner] : [];

        var topics = await _api.SendAsync(HttpMethod.Get, $"{Api}/repos/{target}/topics", null, cancellationToken, anonymous: true).ConfigureAwait(false);
        var prefix = ListingOwnership.TopicFor(string.Empty);
        var logins = (topics.Status == HttpStatusCode.NotFound ? [] : GitHubApi.Parse<TopicsDto>(GitHubApi.Ensure(topics)).Names)
            .Where(topic => topic.StartsWith(prefix, StringComparison.Ordinal))
            .Select(topic => topic[prefix.Length..])
            .ToList();

        // A fork inherits the marker file of its parent, so only its topics name an owner.
        if (!repository.Fork)
        {
            try
            {
                var marker = await _api.ReadFileAsync(target, ListingOwnership.MarkerPath, null, cancellationToken, anonymous: true).ConfigureAwait(false);
                if (marker?.Text is { } text && MarkerLogin(text, id) is { } claimed)
                    logins.Add(claimed);
            }
            catch (GitHubApiException) when (logins.Count > 0)
            {
                // the topics still name owners
            }
        }

        return logins
            .Where(login => Login().IsMatch(login))
            .DistinctBy(login => login.ToLowerInvariant())
            .OrderBy(login => login.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether <paramref name="path"/> is a listing document that <see cref="CheckPullRequestAsync"/> takes, which needs a lower-case .toml as the checks do.</summary>
    internal static bool IsListingPath(string? path)
    {
        var folder = ListingDraft.ListingsFolder + "/";
        var name = path is not null && path.StartsWith(folder, StringComparison.Ordinal) ? path[folder.Length..] : null;
        return name is { Length: > 5 } && name.EndsWith(".toml", StringComparison.Ordinal) && !name.Contains('/', StringComparison.Ordinal);
    }

    /// <summary>The listing at <paramref name="reference"/> of content-index, or null when it is not there.</summary>
    /// <exception cref="GitHubApiException">It could not be read, or it does not parse.</exception>
    private async Task<ListingDraft?> ReadListingAsync(string path, string reference, CancellationToken cancellationToken)
    {
        if (await _api.ReadFileAsync(ListingPullRequestLinks.Repository, path, reference, cancellationToken).ConfigureAwait(false) is not { } file)
            return null;
        if (file.Text is null)
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, $"{path} is no UTF-8 text");

        try
        {
            return ListingDraft.FromDocument(_format.Read(file.Text));
        }
        catch (FormatException exception)
        {
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, exception.Message, innerException: exception);
        }
    }

    private static Uri? ForumsThreadOf(ListingDraft? draft) =>
        draft?.LinkOf("forums") is { } link && ForumsThreadLink.ThreadOf(link) is not null ? new Uri(link) : null;

    /// <summary>RFC 0048: an edit proves control of the listed host, and of the new one when it moves.</summary>
    private async Task<ListingOwnership> VerifyChangeAsync(ListingDraft submitted, ListingDraft? listed, GitHubAccount author, CancellationToken cancellationToken)
    {
        if (listed is null)
            return await VerifyAsync(submitted, author, cancellationToken).ConfigureAwait(false);

        var current = await VerifyAsync(listed, author, cancellationToken).ConfigureAwait(false);
        var listedHost = ListingAuthority.Of(listed);
        var submittedHost = ListingAuthority.Of(submitted);
        if (listedHost is null ? submittedHost is null : listedHost.IsSameHost(submittedHost))
            return current;

        if (current.State != ListingOwnershipState.Verified
            && !await IsRenamedIntoAsync(listedHost, submittedHost, cancellationToken).ConfigureAwait(false))
        {
            return current;
        }

        return await VerifyAsync(submitted, author, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ListingOwnership> VerifyAsync(ListingDraft draft, GitHubAccount author, CancellationToken cancellationToken)
    {
        return ListingAuthority.Of(draft) switch
        {
            { Kind: ListingAuthority.GitHub } github => await VerifyRepositoryAsync(github.Target, draft.Id, author, cancellationToken).ConfigureAwait(false),
            { Kind: ListingAuthority.SpaceDock } spaceDock => await VerifySpaceDockAsync(spaceDock.Target, draft.Id, author, cancellationToken).ConfigureAwait(false),
            _ => new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoHost),
        };
    }

    private async Task<ListingOwnership> VerifyRepositoryAsync(string target, string id, GitHubAccount author, CancellationToken cancellationToken)
    {
        var reply = await _api.SendAsync(HttpMethod.Get, $"{Api}/repos/{target}", null, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return NotVerified(ListingOwnershipProblem.RepositoryMissing, target);

        var repository = GitHubApi.Parse<RepositoryDto>(GitHubApi.Ensure(reply));
        if (!string.Equals(repository.FullName, target, StringComparison.OrdinalIgnoreCase))
            return NotVerified(ListingOwnershipProblem.RepositoryRenamed, target) with { RenamedTo = repository.FullName };

        // The owner of a fork is the account that forked it, and GitHub copies no topic to a fork, so both proofs hold on a fork (RFC 0079).
        if (repository.Owner?.Id == author.Id)
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: target);

        var topics = await _api.SendAsync(HttpMethod.Get, $"{Api}/repos/{target}/topics", null, cancellationToken, anonymous: true).ConfigureAwait(false);
        var names = topics.Status == HttpStatusCode.NotFound ? [] : GitHubApi.Parse<TopicsDto>(GitHubApi.Ensure(topics)).Names;
        if (names.Contains(ListingOwnership.TopicFor(author.Login), StringComparer.Ordinal))
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: target);

        // A fork inherits the marker file of its parent, so the marker file proves nothing there.
        if (repository.Fork)
            return NotVerified(ListingOwnershipProblem.RepositoryFork, target);

        var marker = await _api.ReadFileAsync(target, ListingOwnership.MarkerPath, null, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (marker?.Text is { } text && MarkerNames(text, id, author.Login))
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.MarkerFile, Repository: target);

        return NotVerified(ListingOwnershipProblem.NoProof, target);
    }

    /// <summary>A SpaceDock mod binds to the GitHub repository of its source code link, which only its authors can set.</summary>
    private async Task<ListingOwnership> VerifySpaceDockAsync(string modId, string id, GitHubAccount author, CancellationToken cancellationToken)
    {
        var (repository, failure) = await SpaceDockRepositoryAsync(modId, cancellationToken).ConfigureAwait(false);
        if (repository is null)
            return failure!;

        var result = await VerifyRepositoryAsync(repository, id, author, cancellationToken).ConfigureAwait(false);
        return result with { SpaceDockMod = modId };
    }

    /// <summary>The GitHub repository that the source code link of a SpaceDock mod names, or the result that says why there is none.</summary>
    private async Task<(string? Repository, ListingOwnership? Failure)> SpaceDockRepositoryAsync(string modId, CancellationToken cancellationToken)
    {
        var unusable = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.SpaceDockModUnusable, SpaceDockMod: modId);
        if (modId.Length == 0 || !modId.All(char.IsAsciiDigit))
            return (null, unusable);

        SpaceDockModDto? mod;
        try
        {
            using var response = await _http.GetAsync($"{SpaceDockModRepository.BaseUrl}/api/mod/{modId}", cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, unusable);

            var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            if (!response.IsSuccessStatusCode && !refused)
                return (null, ListingOwnership.Unknown);

            mod = JsonSerializer.Deserialize<SpaceDockModDto>(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), GitHubApi.Json);
            if (mod is null || (refused && !mod.IsError))
                return (null, ListingOwnership.Unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return (null, ListingOwnership.Unknown);
        }

        if (mod.IsError)
            return (null, unusable);
        if (mod.Id?.ToString() != modId)
            return (null, ListingOwnership.Unknown);
        if (!mod.IsGame(SpaceDockGameId))
            return (null, unusable);

        // A link that is set but is no text gets no verdict from the checks either.
        if (IsSet(mod.SourceCode) && mod.SourceCode!.Value.ValueKind != JsonValueKind.String)
            return (null, ListingOwnership.Unknown);

        var link = IsSet(mod.SourceCode) ? mod.SourceCode!.Value.GetString() : null;
        return ListingAuthority.GitHubRepositoryOf(link) is { } repository
            ? (repository, null)
            : (null, unusable with { Problem = ListingOwnershipProblem.SpaceDockNoSourceLink });
    }

    /// <summary>GitHub answers the old name of a renamed or transferred repository with the new one.</summary>
    private async Task<bool> IsRenamedIntoAsync(ListingAuthority? listed, ListingAuthority? submitted, CancellationToken cancellationToken)
    {
        if (listed?.Kind != ListingAuthority.GitHub || submitted?.Kind != ListingAuthority.GitHub)
            return false;

        var reply = await _api.SendAsync(HttpMethod.Get, $"{Api}/repos/{listed.Target}", null, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return false;

        var repository = GitHubApi.Parse<RepositoryDto>(GitHubApi.Ensure(reply));
        return string.Equals(repository.FullName, submitted.Target, StringComparison.OrdinalIgnoreCase);
    }

    private bool MarkerNames(string text, string id, string login) =>
        MarkerLogin(text, id) is { } claimed && string.Equals(claimed, login, StringComparison.OrdinalIgnoreCase);

    /// <summary>The login a marker file names for the listing, or null. A marker that names only the login covers every listing of the repository.</summary>
    private string? MarkerLogin(string text, string id)
    {
        AuthoredTable marker;
        try
        {
            marker = _format.Read(text);
        }
        catch (FormatException)
        {
            return null;
        }

        var claimed = IsSet(marker["login"]) ? marker["login"] : marker["account"];
        var identifier = IsSet(marker["id"]) ? marker["id"] : marker["listing"];
        return claimed is string account && (identifier is not string named || string.Equals(named, id, StringComparison.OrdinalIgnoreCase))
            ? account
            : null;
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

    /// <summary>Whether a JSON value is true as Python reads it.</summary>
    private static bool IsSet(JsonElement? value) => value is { } element && element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => element.GetString()!.Length > 0,
        JsonValueKind.Number => element.GetDouble() != 0,
        JsonValueKind.Array => element.GetArrayLength() > 0,
        JsonValueKind.Object => element.EnumerateObject().Any(),
        _ => false,
    };

    [GeneratedRegex("^(?!.*--)[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$")]
    private static partial Regex Login();

    private static ListingOwnership NotVerified(ListingOwnershipProblem problem, string repository) =>
        new(ListingOwnershipState.NotVerified, Problem: problem, Repository: repository);

    private sealed class OwnerDto
    {
        public long Id { get; set; }

        public string? Login { get; set; }

        public string? Type { get; set; }
    }

    private sealed class RepositoryDto
    {
        public string FullName { get; set; } = string.Empty;

        public bool Fork { get; set; }

        public OwnerDto? Owner { get; set; }
    }

    private sealed class TopicsDto
    {
        public List<string> Names { get; set; } = [];
    }

    /// <summary>The fields the check reads, kept as JSON so that each reads with the truth and equality of tools/ownership.py.</summary>
    private sealed class SpaceDockModDto
    {
        public JsonElement? Id { get; set; }

        public JsonElement? GameId { get; set; }

        public JsonElement? SourceCode { get; set; }

        public JsonElement? Error { get; set; }

        public bool IsError => IsSet(Error);

        public bool IsGame(long id) => GameId is { ValueKind: JsonValueKind.Number } game && game.GetDouble() == id;
    }
}
