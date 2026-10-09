using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Borea.Core.GitHub;
using Borea.Core.Mods;
using Borea.Core.Secrets;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

/// <summary>Keeps the steward changes in memory: each opened change is an open pull request until it merges, and the others then conflict.</summary>
internal sealed class FakeIndexStatusEditor : IIndexStatusEditor
{
    public List<IndexStatusEntry> Entries { get; } = [];

    public List<IndexStatusPullRequest> Pulls { get; } = [];

    public List<IndexStatusChange> Checked { get; } = [];

    public List<IndexStatusChange> Opened { get; } = [];

    public int Reads { get; private set; }

    public Func<IndexStatusChange, IndexStatusCheck>? Check { get; set; }

    public StewardException? ReadFailure { get; set; }

    public StewardException? CheckFailure { get; set; }

    public Exception? OpenFailure { get; set; }

    public TaskCompletionSource? HoldOpen { get; set; }

    /// <summary>The ids that the signed-in steward owns.</summary>
    public List<string> Owned { get; } = [];

    /// <summary>The ids of each owner lookup.</summary>
    public List<IReadOnlyList<string>> OwnedAsked { get; } = [];

    public StewardException? OwnedFailure { get; set; }

    public void Merge(int number)
    {
        Pulls.RemoveAll(pull => pull.Number == number);
        for (var index = 0; index < Pulls.Count; index++)
            Pulls[index] = Pulls[index] with { Conflicts = true };
    }

    public Task<IndexStatusOverview> ReadAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return ReadFailure is { } failure ? Task.FromException<IndexStatusOverview>(failure) : Task.FromResult(new IndexStatusOverview([.. Entries], [.. Pulls]));
    }

    public Task<IndexStatusCheck> CheckAsync(IndexStatusChange change, CancellationToken cancellationToken = default)
    {
        Checked.Add(change);
        return CheckFailure is { } failure
            ? Task.FromException<IndexStatusCheck>(failure)
            : Task.FromResult(Check?.Invoke(change) ?? new IndexStatusCheck(null, [.. Pulls], [], IsOwner: false));
    }

    public async Task<IndexStatusPullRequest> OpenAsync(IndexStatusChange change, CancellationToken cancellationToken = default)
    {
        if (HoldOpen is { } hold)
            await hold.Task;
        if (OpenFailure is { } failure)
            throw failure;

        // the real editor checks the reason only here, because the dialog checks the change before the reason is typed
        if (!IndexStatusChange.IsValidReason(change.Reason))
            throw new IndexStatusRefusedException(IndexStatusRefusal.InvalidReason, change.Id, change.Version);

        Opened.Add(change);
        var pull = new IndexStatusPullRequest(Opened.Count, new Uri($"https://github.com/KSAModding/content-index/pull/{Opened.Count}"), change.Title, "octocat", Conflicts: false);
        Pulls.Add(pull);
        return pull;
    }

    public Task<IReadOnlyList<string>> OwnedAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        OwnedAsked.Add([.. ids]);
        return OwnedFailure is { } failure
            ? Task.FromException<IReadOnlyList<string>>(failure)
            : Task.FromResult<IReadOnlyList<string>>(ids.Where(id => Owned.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList());
    }
}

/// <summary>
/// Amends release files that each say {"version": "..."} in memory: a change yanks them, and a release that is already yanked stays as it is.
/// It records each request it previews and each preview it opens.
/// </summary>
internal sealed class FakeReleaseAmendments : IReleaseAmendments
{
    public List<string> Releases { get; } = ["1.2.0", "1.1.0", "1.0.0"];

    /// <summary>The releases that are already yanked on main.</summary>
    public HashSet<string> Yanked { get; } = [];

    public List<ReleaseAmendmentRequest> Previewed { get; } = [];

    public List<ReleaseAmendmentPreview> Opened { get; } = [];

    public List<string> Owners { get; } = ["alice"];

    public Exception? ReleasesFailure { get; set; }

    /// <summary>The text of a release file by version, in place of the one that only says its version.</summary>
    public Dictionary<string, string> Texts { get; } = [];

    /// <summary>The game release list of the base branch that the release files come with.</summary>
    public List<string> GameVersions { get; } = [];

    public Exception? FilesFailure { get; set; }

    public Exception? PreviewFailure { get; set; }

    /// <summary>Thrown by the next open only, as when main changed once.</summary>
    public Exception? OpenFailure { get; set; }

    public TaskCompletionSource? HoldOpen { get; set; }

    public Task<IReadOnlyList<string>> ReleasesAsync(string listingId, CancellationToken cancellationToken = default) =>
        ReleasesFailure is { } failure ? Task.FromException<IReadOnlyList<string>>(failure) : Task.FromResult<IReadOnlyList<string>>([.. Releases]);

    public Task<ReleaseFiles> ReleaseFilesAsync(string listingId, CancellationToken cancellationToken = default) =>
        FilesFailure is { } failure
            ? Task.FromException<ReleaseFiles>(failure)
            : Task.FromResult(new ReleaseFiles(
                [.. Releases.Select(version => new ReleaseFile(version, $"releases/{listingId}/{version}.json", Texts.GetValueOrDefault(version) ?? Text(version, Yanked.Contains(version))))],
                [.. GameVersions]));

    /// <summary>What the mod.toml of the archive of a release declares, by version. A release that is not here declares nothing.</summary>
    public Dictionary<string, List<LocalModDependency>> Declared { get; } = [];

    /// <summary>The versions whose archive cannot be read.</summary>
    public HashSet<string> UnreadableArchives { get; } = [];

    /// <summary>Holds every archive read until it is set, as a slow download does.</summary>
    public TaskCompletionSource? HoldArchives { get; set; }

    /// <summary>The versions whose archive was read, in order.</summary>
    public List<string> ArchivesRead { get; } = [];

    public async Task<IReadOnlyList<LocalModDependency>> DeclaredDependenciesAsync(ReleaseFile file, CancellationToken cancellationToken = default)
    {
        ArchivesRead.Add(file.Version);
        if (HoldArchives is { } hold)
            await hold.Task;

        return UnreadableArchives.Contains(file.Version)
            ? throw new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.UnreadableArchive, $"{file.Path}: the archive at https://example.com/{file.Version}.zip is gone (HTTP 404)")
            : [.. Declared.GetValueOrDefault(file.Version) ?? []];
    }

    public Task<ReleaseAmendmentPreview> PreviewAsync(ReleaseAmendmentRequest request, CancellationToken cancellationToken = default)
    {
        Previewed.Add(request);
        return PreviewFailure is { } failure ? Task.FromException<ReleaseAmendmentPreview>(failure) : Task.FromResult(Derive(request));
    }

    public async Task<ReleaseAmendmentPullRequest> OpenAsync(ReleaseAmendmentPreview preview, CancellationToken cancellationToken = default)
    {
        if (HoldOpen is { } hold)
            await hold.Task;
        if (OpenFailure is { } failure)
        {
            OpenFailure = null;
            throw failure;
        }

        Opened.Add(preview);
        return new ReleaseAmendmentPullRequest(Opened.Count, new Uri($"https://github.com/KSAModding/content-index-releases/pull/{Opened.Count}"), preview.Title);
    }

    public ReleaseAmendmentPreview Derive(ReleaseAmendmentRequest request)
    {
        var selected = request.Selection.Versions ?? (request.Selection.UpToVersion is { } upTo ? Releases.SkipWhile(version => version != upTo).ToList() : Releases);
        var files = selected.Select(version => new ReleaseFilePreview(
            version,
            $"releases/{request.ListingId}/{version}.json",
            Text(version, Yanked.Contains(version)),
            Yanked.Contains(version) ? null : Text(version, yanked: true))).ToList();
        return new ReleaseAmendmentPreview(request, files, [.. Owners]);
    }

    /// <summary>A release file as the stamper writes its bounds: StarMap from 0.4.5 and the derived optional KittenExtensions, and game_max only when given.</summary>
    public static string Stamped(string id, string version, string? gameMax)
    {
        var max = gameMax is null ? string.Empty : $"\n  \"game_max\": \"{gameMax}\",\n  \"game_max_revision\": {gameMax[(gameMax.LastIndexOf('.') + 1)..]},";
        return $$"""
            {
              "id": "{{id}}",
              "version": "{{version}}",
              "game_min": "2026.8.19.5261",
              "game_min_revision": 5261,{{max}}
              "loader": {
                "id": "StarMap",
                "min": "0.4.5",
                "source": "authored"
              },
              "dependencies": [
                {
                  "id": "KittenExtensions",
                  "kind": "optional",
                  "source": "derived"
                }
              ]
            }

            """;
    }

    /// <summary>The release file with one more required dependency: an authored one from 0.2.2, or a derived one as the archive's mod.toml declares it.</summary>
    public static string WithDependency(string text, string id, string source)
    {
        var document = JsonNode.Parse(text)!.AsObject();
        var entry = new JsonObject { ["id"] = id, ["kind"] = "required" };
        if (source == "authored")
            entry["min"] = "0.2.2";
        entry["source"] = source;
        document["dependencies"]!.AsArray().Add(entry);
        return document.ToJsonString();
    }

    private static string Text(string version, bool yanked) =>
        yanked ? $"{{\n  \"version\": \"{version}\",\n  \"yanked\": true\n}}\n" : $"{{\n  \"version\": \"{version}\"\n}}\n";
}

/// <summary>Answers the queue from its lists and records each filter it was asked for.</summary>
internal sealed class FakeStewardQueue : IStewardQueue
{
    public List<StewardQueueItem> Items { get; } = [];

    public List<StewardQueueFailure> Failures { get; } = [];

    public List<StewardQueueFilter> Filters { get; } = [];

    public StewardException? Failure { get; set; }

    public TaskCompletionSource? Hold { get; set; }

    public static StewardQueueItem Item(int number, bool needsSteward = true, string repository = "KSAModding/content-index", string? verdict = null) =>
        new(repository, number, new Uri($"https://github.com/{repository}/pull/{number}"), $"Pull {number}", "alice", DateTimeOffset.UtcNow.AddDays(-3), IsDraft: false, needsSteward, [StewardQueueKind.Listing], HasOtherFiles: false, verdict);

    public async Task<StewardQueue> ListAsync(StewardQueueFilter filter = StewardQueueFilter.NeedsSteward, CancellationToken cancellationToken = default)
    {
        Filters.Add(filter);
        if (Hold is { } hold)
            await hold.Task;
        if (Failure is { } failure)
            throw failure;

        return new StewardQueue([.. Items.Where(item => filter == StewardQueueFilter.AllOpen || item.NeedsSteward)], [.. Failures]);
    }
}

/// <summary>Answers each review from <see cref="Reviews"/> and records every read.</summary>
internal sealed class FakePullRequestReviews : IPullRequestReviews
{
    public Dictionary<(string Repository, int Number), PullRequestReview> Reviews { get; } = [];

    public List<(string Repository, int Number)> Reads { get; } = [];

    public StewardException? Failure { get; set; }

    public const string Head = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>A pull request of alice with no files, no documents, a green validate and no verdict.</summary>
    public static PullRequestReview Review(int number, string repository = "KSAModding/content-index", string? headRepository = null) =>
        new(repository, number, new Uri($"https://github.com/{repository}/pull/{number}"), $"Pull {number}", "alice", PullRequestState.Open, IsDraft: false, [], "main", Head, headRepository ?? repository, new ValidateStatus(ValidateState.Success), null, null, [], []);

    public static PullRequestFile File(PullRequestReview review, string path, string? patch, string status = "added", string? previousPath = null) =>
        new(path, status, previousPath, 4, 1, patch, PullRequestFile.DiffUrlOf(review.Url, path));

    public Task<PullRequestReview> ReadAsync(string repository, int number, CancellationToken cancellationToken = default)
    {
        Reads.Add((repository, number));
        return Failure is { } failure ? Task.FromException<PullRequestReview>(failure) : Task.FromResult(Reviews[(repository, number)]);
    }
}

/// <summary>Records every action it was sent, and answers each with the next of <see cref="Failures"/> or a success.</summary>
internal sealed class FakePullRequestActions : IPullRequestActions
{
    public List<(string Action, PullRequestReview PullRequest, string? Text, bool SkipRequiredReview)> Sent { get; } = [];

    public Queue<Exception> Failures { get; } = [];

    public TaskCompletionSource? Hold { get; set; }

    public Task ReviewAsync(PullRequestReview pullRequest, PullRequestReviewKind kind, string? body, CancellationToken cancellationToken = default) =>
        RunAsync(kind.ToString(), pullRequest, body, false);

    public Task MergeAsync(PullRequestReview pullRequest, bool skipRequiredReview, CancellationToken cancellationToken = default) =>
        RunAsync("Merge", pullRequest, null, skipRequiredReview);

    public Task CloseAsync(PullRequestReview pullRequest, string comment, CancellationToken cancellationToken = default) =>
        RunAsync("Close", pullRequest, comment, false);

    private async Task RunAsync(string action, PullRequestReview pullRequest, string? text, bool skipRequiredReview)
    {
        Sent.Add((action, pullRequest, text, skipRequiredReview));
        if (Hold is { } hold)
            await hold.Task;
        if (Failures.TryDequeue(out var failure))
            throw failure;
    }
}

/// <summary>Answers the Watcher tab from its lists and counts the reads.</summary>
internal sealed class FakeWatcherIssues : IWatcherIssues
{
    public List<WatcherIssue> Listings { get; } = [];

    public List<WatcherIssue> Watchdog { get; } = [];

    public List<WatcherIssuesFailure> Failures { get; } = [];

    public int Reads { get; private set; }

    public static WatcherIssue Listing(int number, string? listingId) =>
        new(WatcherIssues.ListingsRepository, number, new Uri($"https://github.com/{WatcherIssues.ListingsRepository}/issues/{number}"), $"{listingId ?? "Something"}: the watcher found a problem", DateTimeOffset.UtcNow.AddHours(-2), listingId);

    public static WatcherIssue WatchdogIssue(int number) =>
        new(WatcherIssues.WatchdogRepository, number, new Uri($"https://github.com/{WatcherIssues.WatchdogRepository}/issues/{number}"), "The watcher is not ticking", DateTimeOffset.UtcNow.AddMinutes(-50), null);

    public Task<WatcherIssues> ListAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(new WatcherIssues([.. Listings], [.. Watchdog], [.. Failures]));
    }
}

/// <summary>Answers the Reports tab from its list and counts the reads.</summary>
internal sealed class FakeIndexReports : IIndexReports
{
    public const string TakedownBody = "### Listing id\n\n{0}\n\n### Ground\n\nThe archive carries something harmful\n\n### What is wrong\n\nThe installer runs a script.\n\n### Who you are\n\nA player.";

    public const string DisputeBody = "### Listing id\n\n{0}\n\n### What is disputed\n\nThe id is the folder name of my content, and somebody else listed it\n\n### Your forums thread\n\nhttps://forums.ahwoo.com/threads/measure-tools.123/\n\n### Your claim\n\nI announced it first.\n\n### The other party\n\n_No response_";

    public List<IndexReport> Reports { get; } = [];

    public StewardException? Failure { get; set; }

    public int Reads { get; private set; }

    public static IndexReport Takedown(int number, string listing, string author = "alice") =>
        Report(number, $"[Takedown] {listing}", string.Format(System.Globalization.CultureInfo.InvariantCulture, TakedownBody, listing), author);

    public static IndexReport Dispute(int number, string listing, string author = "bob") =>
        Report(number, $"[Dispute] {listing}", string.Format(System.Globalization.CultureInfo.InvariantCulture, DisputeBody, listing), author);

    public static IndexReport Report(int number, string title, string body, string author = "alice") =>
        IndexReport.FromIssue(number, new Uri($"https://github.com/{IndexReport.Repository}/issues/{number}"), title, author, DateTimeOffset.UtcNow.AddDays(-2), body)!;

    public Task<IReadOnlyList<IndexReport>> ListAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Failure is { } failure ? Task.FromException<IReadOnlyList<IndexReport>>(failure) : Task.FromResult<IReadOnlyList<IndexReport>>([.. Reports]);
    }
}

/// <summary>A signed-in session that answers the main ruleset of each index repository with its bypass.</summary>
internal sealed class StewardSession : IGitHubSession
{
    public bool IsAvailable => true;

    public string ManageAccessUrl => "https://github.com/settings/apps/authorizations";

    public string InstallUrl => "https://github.com/apps/borea-test/installations/new";

    public string InstallUrlFor(long repositoryId) => InstallUrl;

    public GitHubSessionState State { get; private set; } = GitHubSessionState.SignedOut;

    public Dictionary<string, string> Bypass { get; } = new()
    {
        ["KSAModding/content-index"] = "always",
        ["KSAModding/content-index-releases"] = "always",
    };

    public event EventHandler? StateChanged;

    public bool KeepSignedIn { get; set; }

    public SecretStoreProblem? KeepSignedInProblem => null;

    public event EventHandler? KeepSignedInProblemChanged
    {
        add { }
        remove { }
    }

    public Task<GitHubResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default) => Task.FromResult(GitHubResumeOutcome.NothingKept);

    public void SignInDirectly()
    {
        State = GitHubSessionState.SignedInAs("octocat");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = true, CancellationToken cancellationToken = default)
    {
        SignInDirectly();
        return Task.FromResult(new GitHubSignInResult(GitHubSignInOutcome.SignedIn, "octocat"));
    }

    public void SignOut()
    {
        State = GitHubSessionState.SignedOut;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var repository = Bypass.Keys.FirstOrDefault(name => url.StartsWith($"https://api.github.com/repos/{name}/rulesets", StringComparison.Ordinal));
        var body = repository is null ? null
            : url.EndsWith("/rulesets?per_page=100&page=1", StringComparison.Ordinal) ? """[{"id":1,"target":"branch","enforcement":"active"}]"""
            : """{"id":1,"target":"branch","enforcement":"active","conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}},"current_user_can_bypass":""" + $"\"{Bypass[repository]}\"}}";
        return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
        {
            Content = new StringContent(body ?? """{"message":"Not Found"}""", Encoding.UTF8, "application/json"),
        });
    }
}
