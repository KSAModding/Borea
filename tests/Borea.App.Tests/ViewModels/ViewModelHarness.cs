using System.Globalization;
using System.Net;
using System.Text;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Composition;
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

    public static async Task<ViewModelHarness> CreateAsync()
    {
        var harness = new ViewModelHarness();
        Directory.CreateDirectory(harness.Root);
        harness.Services = await harness.BuildServicesAsync();
        var preferences = (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences;
        harness.ViewModel = new MainViewModel(
            harness.Localization,
            new RegionalFormatService(harness.Localization),
            harness.Services.AppPreferences,
            preferences,
            harness.Services,
            async () => harness.Services = await harness.BuildServicesAsync());
        await harness.ViewModel.LoadAsync();
        return harness;
    }

    public Task<BoreaServices> BuildServicesAsync() =>
        BoreaServices.BuildAsync(Root, new IndexOnlyHandler(), new FakeSpaceDock());

    public void Dispose()
    {
        Services.Dispose();
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    private static string SnapshotFixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json");

    /// <summary>
    /// Serves the index snapshot and fails every other request, the way a
    /// download from a host the tests cannot reach fails.
    /// </summary>
    private sealed class IndexOnlyHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri.StartsWith("https://ksamodding.github.io/content-index-releases/", StringComparison.Ordinal) != true)
                throw new HttpRequestException($"No network in tests: {request.RequestUri}");

            var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(snapshot, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// SpaceDock stand-in. 4253 is the SpaceDock copy of AdvancedFlightComputer,
    /// which the index lists too; 5000 is only on SpaceDock and serves its
    /// description per mod, like the real site.
    /// </summary>
    internal sealed class FakeSpaceDock : IModRepository
    {
        public const string MirroredId = "4253";
        public const string OwnId = "5000";
        public const string OwnDescription = "## Aircraft HUD\n\nAdds a **HUD**.";

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

        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>(
            [
                Listing(MirroredId, "AdvancedFlightComputer", null),
                Listing(OwnId, "Aircraft HUD", null),
            ]);

        public Task<ModMetadata?> GetListingAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult(modId == OwnId ? Listing(OwnId, "Aircraft HUD", OwnDescription) : null);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModVersion>>([]);

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
