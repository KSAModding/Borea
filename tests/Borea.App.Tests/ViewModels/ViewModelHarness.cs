using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

/// <summary>
/// A <see cref="MainViewModel"/> over real services in a temporary Borea root.
/// The content index comes from the shared snapshot fixture; SpaceDock is a
/// fake that knows one mirrored listing and one listing of its own.
/// </summary>
internal sealed class ViewModelHarness : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "BoreaAppTest_" + Guid.NewGuid());

    public BoreaServices Services { get; private set; } = null!;

    public MainViewModel ViewModel { get; private set; } = null!;

    public LocalizationService Localization { get; } = new(CultureInfo.GetCultureInfo("en"));

    /// <summary>The SpaceDock stand-in every service graph of this harness reads.</summary>
    public FakeSpaceDock SpaceDock { get; } = new();

    /// <summary>Every request the services sent.</summary>
    public ConcurrentQueue<Uri> Requests { get; } = new();

    private Func<HttpRequestMessage, HttpResponseMessage?>? _respond;

    private Func<string, string>? _editSnapshot;

    private Borea.Storage.Launch.IProcessStarter? _processStarter;

    private string? _sharedProfileRoot;

    public const string OfflineMessage = "The content index host is offline.";

    /// <summary>Fails every content index request with <see cref="OfflineMessage"/>.</summary>
    public bool IndexOffline { get; set; }

    /// <summary>The folders the install detector checks. Empty unless a test adds some.</summary>
    public FakeInstallCandidates Candidates { get; } = new();

    /// <summary>The image source every service graph of this harness uses, so no image request leaves the test.</summary>
    public FakeImageSource Images { get; } = new();

    /// <param name="seed">Writes settings the view model should start from; the services are rebuilt after it ran.</param>
    /// <param name="respond">Answers a request outside the content index. Null fails it.</param>
    /// <param name="editSnapshot">Changes the index snapshot before it is served.</param>
    /// <param name="indexOffline">The first value of <see cref="IndexOffline"/>.</param>
    /// <param name="candidates">Adds the folders the install detector checks, before the first load.</param>
    /// <param name="processStarter">Starts the launchers' processes. Null starts real ones.</param>
    /// <param name="waitForDetection">False returns while the game detection of the first load may still run.</param>
    /// <param name="sharedProfileRoot">The game profile folder. Null puts it into the temporary root.</param>
    public static async Task<ViewModelHarness> CreateAsync(Func<BoreaServices, Task>? seed = null, Func<HttpRequestMessage, HttpResponseMessage?>? respond = null, Func<string, string>? editSnapshot = null, bool indexOffline = false, Action<ViewModelHarness>? candidates = null, Borea.Storage.Launch.IProcessStarter? processStarter = null, bool waitForDetection = true, string? sharedProfileRoot = null)
    {
        var harness = new ViewModelHarness { _respond = respond, _editSnapshot = editSnapshot, IndexOffline = indexOffline, _processStarter = processStarter, _sharedProfileRoot = sharedProfileRoot };
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

    /// <summary>Adds a curated tag vocabulary for mods to the snapshot, in the given order.</summary>
    public static Func<string, string> CuratedTags(params (string Tag, string Name)[] tags) =>
        json => "{ \"tags\": " + $$"""{ "spec_version": 1, "mod": [{{string.Join(", ", tags.Select(tag => $$"""{ "tag": "{{tag.Tag}}", "name": "{{tag.Name}}", "meaning": "{{tag.Name}} content." }"""))}}] }""" + "," + json.TrimStart()[1..];

    public Task<BoreaServices> BuildServicesAsync() =>
        BoreaServices.BuildAsync(Root, new IndexOnlyHandler(this), SpaceDock, Candidates, processStarter: _processStarter, images: Images, sharedProfileRoot: _sharedProfileRoot ?? Path.Combine(Root, "GameProfile"), isGameProcessRunning: () => false, isOtherBoreaRunning: () => false);

    public void Dispose()
    {
        // a language or theme change saves in the background; let it finish before the folder goes
        ViewModel?.WhenPreferencesSavedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenUpdateCheckedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenGameBuildCheckedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenNewerGamePatchNotesLoadedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenAnnouncementsCheckedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenReleaseChannelSavedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenContentUpdatesCheckedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenPlaytimeLoadedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenInstanceSizesLoadedAsync().GetAwaiter().GetResult();
        ViewModel?.WhenGameDetectedAsync().GetAwaiter().GetResult();
        ViewModel?.Tasks.WhenSavedAsync().GetAwaiter().GetResult();
        Services.Dispose();
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
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

            if (owner.IndexOffline)
                throw new HttpRequestException(OfflineMessage);

            var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(owner._editSnapshot?.Invoke(snapshot) ?? snapshot, Encoding.UTF8, "application/json"),
            };
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

        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>(
            [
                Listing(MirroredId, "AdvancedFlightComputer", null),
                Listing(OwnId, "Aircraft HUD", null),
            ]);

        public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult(modId == OwnId ? Listing(OwnId, "Aircraft HUD", OwnDescription) : null);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReleasesOf(modId).FirstOrDefault(release => !release.Yanked));

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
            ReleaseFailure?.Invoke(version) is { } failure
                ? Task.FromException<ModVersionMetadata?>(failure)
                : Task.FromResult(ReleasesOf(modId).FirstOrDefault(release => release.Version == version));

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModVersion>>(ReleasesOf(modId).Select(release => release.Version).ToList());

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
