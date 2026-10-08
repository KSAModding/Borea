using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Borea.App.Formatting;
using Borea.App.Links;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

/// <summary>
/// A <see cref="MainViewModel"/> over real services in a temporary Borea root.
/// The content index comes from the shared snapshot fixture; SpaceDock is a
/// fake that knows one mirrored listing and one listing of its own. A test
/// builds every further view model on these services through
/// <see cref="NewViewModel"/> and never with <c>new MainViewModel</c>, because
/// only then does <see cref="Dispose"/> wait for its preference saves.
/// </summary>
internal sealed class ViewModelHarness : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "BoreaAppTest_" + Guid.NewGuid());

    public BoreaServices Services { get; private set; } = null!;

    public MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>The view models that <see cref="NewViewModel"/> built.</summary>
    private readonly List<MainViewModel> _otherViewModels = [];

    public LocalizationService Localization { get; } = new(CultureInfo.GetCultureInfo("en"));

    /// <summary>The SpaceDock stand-in every service graph of this harness reads.</summary>
    public FakeSpaceDock SpaceDock { get; } = new();

    /// <summary>Every request the services sent.</summary>
    public ConcurrentQueue<Uri> Requests { get; } = new();

    private Func<HttpRequestMessage, HttpResponseMessage?>? _respond;

    private Func<string, string>? _editSnapshot;

    private Borea.Storage.Launch.IProcessStarter? _processStarter;
    private IGamePlatform? _gamePlatform;
    private Borea.Core.Launch.ILauncher? _loaderLauncher;
    private Borea.Core.Launch.ISharedProfileLauncher? _sharedProfileLauncher;
    private Borea.Core.ModLoaders.ILoaderConfigurator? _loaderConfigurator;
    private Func<Borea.Core.Planning.IInstallPlanner, Borea.Core.Planning.IInstallPlanner>? _wrapInstallPlanner;

    private string? _sharedProfileRoot;

    private IGitHubSession? _gitHub;

    private IListingPublisher? _listingPublisher;
    private Borea.Core.Stewardship.IIndexStatusEditor? _indexStatusEditor;
    private Borea.Core.Stewardship.IStewardQueue? _stewardQueue;
    private Borea.Core.Stewardship.IWatcherIssues? _watcherIssues;
    private Borea.Core.Stewardship.IPullRequestReviews? _pullRequestReviews;
    private Borea.Core.Stewardship.IPullRequestActions? _pullRequestActions;
    private Borea.Core.Stewardship.IIndexReports? _indexReports;
    private Borea.Core.Stewardship.IReleaseAmendments? _releaseAmendments;
    private Borea.Core.Updates.IBoreaReleaseCheck? _releaseCheck;
    private Borea.Core.Updates.ISelfUpdater? _selfUpdater;
    private IContentImageSource? _images;

    public const string OfflineMessage = "The content index host is offline.";

    /// <summary>Fails every content index request with <see cref="OfflineMessage"/>.</summary>
    public bool IndexOffline { get; set; }

    /// <summary>
    /// Answers content index requests with an ETag of the served snapshot, and
    /// with 304 Not Modified when the request carries that ETag.
    /// </summary>
    public bool IndexEtag { get; set; }

    /// <summary>Runs before a content index request is answered, so that a test can hold it.</summary>
    public Func<Task>? IndexRequest { get; set; }

    /// <summary>The folders the install detector checks. Empty unless a test adds some.</summary>
    public FakeInstallCandidates Candidates { get; } = new();

    /// <summary>The image source every service graph of this harness uses, so no image request leaves the test.</summary>
    public FakeImageSource Images { get; } = new();

    /// <summary>What the library folder changer asks before it moves the library.</summary>
    public Func<bool> IsOtherBoreaRunning { get; set; } = () => false;

    /// <summary>Whether a KSA or StarMap process runs, whoever started it.</summary>
    public Func<bool> IsGameProcessRunning { get; set; } = () => false;

    /// <param name="seed">Writes settings the view model should start from; the services are rebuilt after it ran.</param>
    /// <param name="respond">Answers a request outside the content index. Null fails it.</param>
    /// <param name="editSnapshot">Changes the index snapshot before it is served.</param>
    /// <param name="indexOffline">The first value of <see cref="IndexOffline"/>.</param>
    /// <param name="indexEtag">The first value of <see cref="IndexEtag"/>.</param>
    /// <param name="candidates">Adds the folders the install detector checks, before the first load.</param>
    /// <param name="processStarter">Starts the launchers' processes. Null starts real ones.</param>
    /// <param name="waitForDetection">False returns while the game detection of the first load may still run.</param>
    /// <param name="sharedProfileRoot">The game profile folder. Null puts it into the temporary root.</param>
    /// <param name="gitHub">The GitHub session every service graph of this harness shares. Null builds one per graph for <see cref="Borea.Network.GitHub.BoreaGitHubApp"/>.</param>
    /// <param name="listingPublisher">Opens the listing pull request. Null builds one on the GitHub session.</param>
    /// <param name="releaseCheck">Finds the published Borea releases. Null asks for them through <paramref name="respond"/>.</param>
    /// <param name="selfUpdater">Replaces the Borea build. Null reads the build the tests run from, which is no release archive.</param>
    /// <param name="indexStatusEditor">Opens the steward pull requests of index-status.toml. Null builds one on the GitHub session.</param>
    /// <param name="stewardQueue">Lists the pull requests that wait for a steward. Null builds one on the GitHub session.</param>
    /// <param name="watcherIssues">Lists the issues of the watcher and its watchdog. Null builds one that reads GitHub without the token.</param>
    /// <param name="pullRequestReviews">Reads one pull request for a steward. Null builds one on the GitHub session.</param>
    /// <param name="pullRequestActions">Acts on one pull request for a steward. Null builds one on the GitHub session.</param>
    /// <param name="indexReports">Lists the takedown and id dispute reports. Null builds one that reads GitHub without the token.</param>
    /// <param name="releaseAmendments">Opens the steward amendments of release files. Null builds one on the GitHub session.</param>
    /// <param name="images">The image source of every service graph, which a dispose of the services disposes too. Null uses <see cref="Images"/>.</param>
    /// <param name="gamePlatform">The platform of the game build. Null reads it from the game folder on this system.</param>
    /// <param name="loaderLauncher">Starts the game through a loader. Null starts it with <paramref name="processStarter"/>.</param>
    /// <param name="sharedProfileLauncher">Starts the game without a loader. Null starts it with <paramref name="processStarter"/>.</param>
    /// <param name="loaderConfigurator">What the launch refreshes the loader's game path with. Null uses the one of the graph.</param>
    /// <param name="wrapInstallPlanner">Wraps the planner of every service graph, so that a test can watch the plans. Null plans as Borea does.</param>
    public static async Task<ViewModelHarness> CreateAsync(Func<BoreaServices, Task>? seed = null, Func<HttpRequestMessage, HttpResponseMessage?>? respond = null, Func<string, string>? editSnapshot = null, bool indexOffline = false, bool indexEtag = false, Action<ViewModelHarness>? candidates = null, Borea.Storage.Launch.IProcessStarter? processStarter = null, bool waitForDetection = true, string? sharedProfileRoot = null, IGitHubSession? gitHub = null, IListingPublisher? listingPublisher = null, Borea.Core.Updates.IBoreaReleaseCheck? releaseCheck = null, Borea.Core.Updates.ISelfUpdater? selfUpdater = null, Borea.Core.Stewardship.IIndexStatusEditor? indexStatusEditor = null, Borea.Core.Stewardship.IStewardQueue? stewardQueue = null, Borea.Core.Stewardship.IWatcherIssues? watcherIssues = null, Borea.Core.Stewardship.IPullRequestReviews? pullRequestReviews = null, Borea.Core.Stewardship.IPullRequestActions? pullRequestActions = null, Borea.Core.Stewardship.IIndexReports? indexReports = null, Borea.Core.Stewardship.IReleaseAmendments? releaseAmendments = null, IContentImageSource? images = null, IGamePlatform? gamePlatform = null, Borea.Core.Launch.ILauncher? loaderLauncher = null, Borea.Core.Launch.ISharedProfileLauncher? sharedProfileLauncher = null, Borea.Core.ModLoaders.ILoaderConfigurator? loaderConfigurator = null, Func<Borea.Core.Planning.IInstallPlanner, Borea.Core.Planning.IInstallPlanner>? wrapInstallPlanner = null)
    {
        var harness = new ViewModelHarness { _respond = respond, _editSnapshot = editSnapshot, IndexOffline = indexOffline, IndexEtag = indexEtag, _processStarter = processStarter, _sharedProfileRoot = sharedProfileRoot, _gitHub = gitHub, _listingPublisher = listingPublisher, _releaseCheck = releaseCheck, _selfUpdater = selfUpdater, _indexStatusEditor = indexStatusEditor, _stewardQueue = stewardQueue, _watcherIssues = watcherIssues, _pullRequestReviews = pullRequestReviews, _pullRequestActions = pullRequestActions, _indexReports = indexReports, _releaseAmendments = releaseAmendments, _images = images, _gamePlatform = gamePlatform, _loaderLauncher = loaderLauncher, _sharedProfileLauncher = sharedProfileLauncher, _loaderConfigurator = loaderConfigurator, _wrapInstallPlanner = wrapInstallPlanner };
        Directory.CreateDirectory(harness.Root);
        candidates?.Invoke(harness);
        harness.Services = await harness.BuildServicesAsync();
        if (seed is not null)
        {
            await seed(harness.Services);
            harness.Services.Dispose();
            harness.Services = await harness.BuildServicesAsync();
        }

        var preferences = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        harness.ViewModel = new MainViewModel(
            harness.Localization,
            new RegionalFormatService(harness.Localization),
            harness.Services.AppPreferences,
            preferences.Preferences,
            harness.Services,
            async () => harness.Services = await harness.BuildServicesAsync())
        {
            PreferencesLoadStatus = preferences.Status,
        };
        await harness.ViewModel.LoadAsync();
        if (waitForDetection)
            await harness.ViewModel.WhenGameDetectedAsync();
        return harness;
    }

    /// <summary>
    /// Builds one more view model, for a test that restarts Borea or starts from
    /// other values. <see cref="WhenIdleAsync"/> and <see cref="Dispose"/> wait for
    /// its work too, because Windows cannot delete the folder while a preference
    /// save still has its file open.
    /// </summary>
    /// <param name="appPreferencesRepository">Where it saves its preferences. Null saves none.</param>
    /// <param name="appPreferences">The preferences it starts from. Null starts from none.</param>
    /// <param name="services">The services it works on. Null leaves it without services.</param>
    /// <param name="localization">Null uses <see cref="Localization"/>.</param>
    /// <param name="linkHandler">Registers the borea links. Null registers none.</param>
    public MainViewModel NewViewModel(IAppPreferencesRepository? appPreferencesRepository = null, AppPreferences? appPreferences = null, BoreaServices? services = null, LocalizationService? localization = null, LinkHandler? linkHandler = null)
    {
        localization ??= Localization;
        var viewModel = new MainViewModel(localization, new RegionalFormatService(localization), appPreferencesRepository, appPreferences ?? AppPreferences.Empty, services)
        {
            LinkHandler = linkHandler,
        };
        _otherViewModels.Add(viewModel);
        return viewModel;
    }

    /// <summary>Adds a curated tag vocabulary for mods to the snapshot, in the given order.</summary>
    public static Func<string, string> CuratedTags(params (string Tag, string Name)[] tags) =>
        json => "{ \"tags\": " + $$"""{ "spec_version": 1, "mod": [{{string.Join(", ", tags.Select(tag => $$"""{ "tag": "{{tag.Tag}}", "name": "{{tag.Name}}", "meaning": "{{tag.Name}} content." }"""))}}] }""" + "," + json.TrimStart()[1..];

    /// <summary>When <see cref="MarkGone"/> says the download went from its host.</summary>
    public static readonly DateTimeOffset GoneSince = new(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);

    /// <summary>Marks the download of one release as gone from its host (RFC 0078), and drops its mirrors unless <paramref name="keepMirrors"/>.</summary>
    public static Func<string, string> MarkGone(string modId, string version, bool keepMirrors = false) => json =>
    {
        var root = JsonNode.Parse(json)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;
        var download = listing["releases"]!.AsArray().Single(node => (string?)node!["version"] == version)!["download"]!.AsObject();
        download["unavailable_since"] = "2026-09-23T10:24:00Z";
        if (!keepMirrors)
            download.Remove("mirrors");
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    };

    /// <summary>Removes the authored document of one listing after <paramref name="edit"/>, so the snapshot keeps only its releases.</summary>
    public static Func<string, string> WithoutAuthored(string modId, Func<string, string> edit) => json =>
    {
        var root = JsonNode.Parse(edit(json))!;
        root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!.AsObject().Remove("authored");
        return root.ToJsonString();
    };

    /// <summary>Adds <paramref name="count"/> copies of one listing to the index snapshot, each with the id and the name of the listing plus a number.</summary>
    public static string WithCopies(string json, string listingId, int count)
    {
        var root = JsonNode.Parse(json)!;
        var listings = root["listings"]!.AsArray();
        var original = listings.Single(node => (string?)node!["id"] == listingId)!;
        for (var number = 1; number <= count; number++)
        {
            var id = $"{listingId}{number}";
            var copy = original.DeepClone();
            copy["id"] = id;
            copy["authored"]!["id"] = id;
            copy["authored"]!["name"] = id;
            foreach (var release in copy["releases"]!.AsArray())
            {
                release!["id"] = id;
                release["listing"]!["name"] = id;
            }

            listings.Add(copy);
        }

        return root.ToJsonString();
    }

    public Task<BoreaServices> BuildServicesAsync() =>
        BoreaServices.BuildAsync(Root, new IndexOnlyHandler(this), SpaceDock, Candidates, processStarter: _processStarter, images: _images ?? Images, sharedProfileRoot: _sharedProfileRoot ?? Path.Combine(Root, "GameProfile"), isGameProcessRunning: () => IsGameProcessRunning(), isOtherBoreaRunning: () => IsOtherBoreaRunning(), gitHub: _gitHub, listingPublisher: _listingPublisher, releaseCheck: _releaseCheck, selfUpdater: _selfUpdater, indexStatusEditor: _indexStatusEditor, stewardQueue: _stewardQueue, watcherIssues: _watcherIssues, pullRequestReviews: _pullRequestReviews, pullRequestActions: _pullRequestActions, indexReports: _indexReports, releaseAmendments: _releaseAmendments, gamePlatform: _gamePlatform, loaderLauncher: _loaderLauncher, sharedProfileLauncher: _sharedProfileLauncher, loaderConfigurator: _loaderConfigurator, wrapInstallPlanner: _wrapInstallPlanner);

    /// <summary>
    /// Completes when no background work of a view model of this harness is in
    /// flight. Work that such a task starts is waited for as well, until nothing
    /// is left.
    /// </summary>
    /// <remarks>
    /// A test that renders must call this inside its headless dispatch. The work of
    /// a click continues on the Avalonia dispatcher of that dispatch, and that
    /// dispatcher is gone once the dispatch returned, so what is left behind then
    /// never continues.
    /// </remarks>
    /// <param name="limit">
    /// How long one wait may take, or null for a wait without an end. A wait inside
    /// a dispatch needs no limit, because the dispatcher of that dispatch runs the
    /// work.
    /// </param>
    public async Task WhenIdleAsync(TimeSpan? limit = null)
    {
        for (var pass = 0; pass < IdlePasses; pass++)
        {
            var pending = Pending().ToList();
            if (pending.Count == 0)
                return;

            var all = Task.WhenAll(pending.Select(work => work.Task));
            if (limit is not { } end)
            {
                await all;
                continue;
            }

            try
            {
                await all.WaitAsync(end);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"The background work of the view models did not finish in {end.TotalSeconds:0} seconds, which leaves {string.Join(", ", pending.Select(work => work.Name))}.");
            }
        }

        if (Pending().Any())
            throw new InvalidOperationException($"The view models started new background work in each of {IdlePasses} passes.");
    }

    /// <summary>How often <see cref="WhenIdleAsync"/> waits for work that the last wait started.</summary>
    private const int IdlePasses = 10;

    /// <summary>
    /// The limit of the wait in <see cref="Dispose"/>. The wait needs an end because
    /// a dispose runs outside the headless dispatch, where work that still waits for
    /// the Avalonia dispatcher of that dispatch never continues. A test that reaches
    /// this limit says which work is left instead of stopping the whole test run.
    /// </summary>
    private static readonly TimeSpan DisposeLimit = TimeSpan.FromSeconds(60);

    /// <summary>Every background task of the view models that is not done yet, by name.</summary>
    private IEnumerable<(string Name, Task Task)> Pending()
    {
        if (ViewModel is null)
            yield break;

        foreach (var item in Pending(ViewModel))
            yield return item;

        for (var index = 0; index < _otherViewModels.Count; index++)
            foreach (var (name, task) in Pending(_otherViewModels[index]))
                yield return ($"{name} of view model {index + 2}", task);
    }

    private static IEnumerable<(string Name, Task Task)> Pending(MainViewModel viewModel)
    {
        (string Name, Task Task)[] work =
        [
            ("PreferencesSaved", viewModel.WhenPreferencesSavedAsync()),
            ("UpdateChecked", viewModel.WhenUpdateCheckedAsync()),
            ("SelfUpdateInstalled", viewModel.WhenSelfUpdateInstalledAsync()),
            ("BackupsCleaned", viewModel.WhenBackupsCleanedAsync()),
            ("GameBuildChecked", viewModel.WhenGameBuildCheckedAsync()),
            ("NewerGamePatchNotesLoaded", viewModel.WhenNewerGamePatchNotesLoadedAsync()),
            ("AnnouncementsChecked", viewModel.WhenAnnouncementsCheckedAsync()),
            ("ReleaseChannelSaved", viewModel.WhenReleaseChannelSavedAsync()),
            ("ContentUpdatesChecked", viewModel.WhenContentUpdatesCheckedAsync()),
            ("CrashChecked", viewModel.WhenCrashCheckedAsync()),
            ("PlaytimeLoaded", viewModel.WhenPlaytimeLoadedAsync()),
            ("InstanceSizesLoaded", viewModel.WhenInstanceSizesLoadedAsync()),
            ("GameSettingsPresetsLoaded", viewModel.WhenGameSettingsPresetsLoadedAsync()),
            ("ModStoreChecked", viewModel.WhenModStoreCheckedAsync()),
            ("SharedModStoreSaved", viewModel.WhenSharedModStoreSavedAsync()),
            ("FavoritesSaved", viewModel.WhenFavoritesSavedAsync()),
            ("GameDetected", viewModel.WhenGameDetectedAsync()),
            ("IndexChecked", viewModel.WhenIndexCheckedAsync()),
            ("GitHubResumed", viewModel.WhenGitHubResumedAsync()),
            ("GitHubSignInDone", viewModel.WhenGitHubSignInDoneAsync()),
            ("StewardRoleChecked", viewModel.WhenStewardRoleCheckedAsync()),
            ("LinkRegistrationDone", viewModel.WhenLinkRegistrationDoneAsync()),
            ("TasksSaved", viewModel.Tasks.WhenSavedAsync()),
        ];

        foreach (var item in work)
            if (!item.Task.IsCompleted)
                yield return item;
    }

    public void Dispose()
    {
        // a view model saves its preferences in the background, so every save has to finish before the folder goes
        WhenIdleAsync(DisposeLimit).GetAwaiter().GetResult();
        Services.Dispose();
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
        if (Directory.Exists(Root))
            DeleteFolder(Root);
    }

    /// <summary>The mod links go first, because a recursive delete stops at a junction.</summary>
    private static void DeleteFolder(string folder)
    {
        var everyFolder = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (var link in Directory.EnumerateDirectories(folder, "*", everyFolder).Where(path => new DirectoryInfo(path).LinkTarget is not null).ToList())
            Directory.Delete(link);

        Directory.Delete(folder, recursive: true);
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    internal static string SnapshotFixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json");

    /// <summary>Serves the index snapshot, records every request, and answers or fails the others.</summary>
    private sealed class IndexOnlyHandler(ViewModelHarness owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
                owner.Requests.Enqueue(request.RequestUri);

            if (request.RequestUri?.AbsoluteUri.StartsWith("https://ksamodding.github.io/content-index-releases/", StringComparison.Ordinal) != true)
                return owner._respond?.Invoke(request) ?? throw new HttpRequestException($"No network in tests: {request.RequestUri}");

            if (owner.IndexRequest is { } held)
                await held();

            if (owner.IndexOffline)
                throw new HttpRequestException(OfflineMessage);

            var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath, cancellationToken);
            var body = owner._editSnapshot?.Invoke(snapshot) ?? snapshot;
            if (!owner.IndexEtag)
                return Served(body, etag: null);

            var etag = new EntityTagHeaderValue("\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..16] + "\"");
            return request.Headers.IfNoneMatch.Contains(etag)
                ? new HttpResponseMessage(HttpStatusCode.NotModified) { Headers = { ETag = etag } }
                : Served(body, etag);
        }

        private static HttpResponseMessage Served(string body, EntityTagHeaderValue? etag)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = etag;
            return response;
        }
    }

    internal sealed class FakeInstallCandidates : IInstallCandidateSource
    {
        public List<string> Games { get; } = [];

        public List<string> Loaders { get; } = [];

        /// <summary>Runs while the detector reads the game folders.</summary>
        public Action? Reading { get; set; }

        public IReadOnlyList<string> GetGameDirectories()
        {
            Reading?.Invoke();
            return Games;
        }

        public IReadOnlyList<string> GetLoaderDirectories() => Loaders;
    }

    internal sealed class FakeImageSource : IContentImageSource
    {
        public ConcurrentQueue<(ContentImage Image, bool LoadFromAuthorHosts)> Requests { get; } = new();

        public Func<ContentImage, ContentImageResult> Respond { get; set; } =
            _ => ContentImageResult.Failed(ContentImageFailure.Unavailable, "No network in tests.");

        public Task<ContentImageResult> GetAsync(ContentImage image, bool loadFromAuthorHosts, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue((image, loadFromAuthorHosts));
            return Task.FromResult(loadFromAuthorHosts
                ? Respond(image)
                : ContentImageResult.Failed(ContentImageFailure.DisabledByPreference, "Loading images from author hosts is off."));
        }
    }

    /// <summary>A repository whose version lookups wait for <paramref name="lookup"/> of the mod id.</summary>
    internal sealed class HeldModRepository(IModRepository inner, Func<string, Task> lookup) : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            inner.GetAvailableModsAsync(cancellationToken);

        public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default) =>
            inner.GetListingAsync(modId, cancellationToken);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
            inner.GetLatestReleaseAsync(modId, cancellationToken);

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
            inner.GetReleaseAsync(modId, version, cancellationToken);

        public async Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
        {
            await lookup(modId);
            return await inner.GetAvailableVersionsAsync(modId, cancellationToken);
        }

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            inner.SearchAsync(query, cancellationToken);
    }

    /// <summary>
    /// SpaceDock stand-in. 4253 is the SpaceDock copy of AdvancedFlightComputer,
    /// which the index lists too; 5000 is only on SpaceDock and serves its
    /// description per mod, like the real site. It serves the releases a test
    /// puts into <see cref="Releases"/>.
    /// </summary>
    internal sealed class FakeSpaceDock : IModRepository
    {
        public const string MirroredId = "4253";
        public const string OwnId = "5000";
        public const string OwnDescription = "## Aircraft HUD\n\nAdds a **HUD**.";

        public List<ModVersionMetadata> Releases { get; } = [];

        public Func<ModVersion, Exception?>? ReleaseFailure { get; set; }

        public Func<string, Task>? VersionLookup { get; set; }

        /// <summary>Runs before the catalog is answered, so that a test can hold or fail it.</summary>
        public Func<Task>? Browse { get; set; }

        /// <summary>How often the catalog was read.</summary>
        public int Browses { get; private set; }

        private static ModMetadata Listing(string id, string name, string? description) => new(
            specVersion: 1,
            modId: id,
            source: "spacedock",
            name: name,
            authors: ["Someone"],
            abstractText: name + " abstract",
            license: "GPL-3.0",
            links: new Dictionary<string, string> { ["forums"] = "https://spacedock.info/mod/" + id },
            gameMin: "2026.1.1.1",
            description: description);

        private IEnumerable<ModVersionMetadata> ReleasesOf(string modId) =>
            Releases.Where(release => ModIds.Equals(release.ModId, modId)).OrderByDescending(release => release.Version);

        public async Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default)
        {
            Browses++;
            if (Browse is { } browse)
                await browse();

            return
            [
                Listing(MirroredId, "AdvancedFlightComputer", null),
                Listing(OwnId, "Aircraft HUD", null),
            ];
        }

        public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult(modId == OwnId ? Listing(OwnId, "Aircraft HUD", OwnDescription) : null);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReleasesOf(modId).FirstOrDefault(release => !release.Yanked));

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
            ReleaseFailure?.Invoke(version) is { } failure
                ? Task.FromException<ModVersionMetadata?>(failure)
                : Task.FromResult(ReleasesOf(modId).FirstOrDefault(release => release.Version == version));

        public async Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default)
        {
            if (VersionLookup is { } lookup)
                await lookup(modId);
            return ReleasesOf(modId).Select(release => release.Version).ToList();
        }

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
