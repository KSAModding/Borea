using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class GameBuildBannerTests
{
    private const string MasterServerHost = "ksa-master1.rocketwerkz.com";

    private const string InstalledBuild = "2026.8.3.5117";

    private const string VersionsHost = "raw.githubusercontent.com";

    private const string VersionsPath = "/KSAModding/ksa-versions/main/Content/Versions/";

    /// <summary>The ksa-versions files of the builds above <see cref="InstalledBuild"/>. The snapshot fixture does not list 5400, only the file of 5402 names it.</summary>
    private static readonly Dictionary<int, string> PublishedFiles = new()
    {
        [5438] = ChangeLog("2026.9.10.5438", 5402),
        [5402] = ChangeLog("2026.9.7.5402", 5400),
        [5400] = ChangeLog("2026.9.4.5400", 5348),
        [5348] = ChangeLog("2026.8.22.5348", 5261),
        [5261] = ChangeLog("2026.8.19.5261", 5168),
        [5168] = ChangeLog("2026.8.5.5168", 5117),
    };

    private static string ChangeLog(string build, int fromRevision) =>
        $$"""{ "build": "{{build}}", "date": "2026-09-15", "fromRevision": {{fromRevision}}, "toRevision": {{GameVersion.Parse(build).Revision}}, "commits": [ { "rev": {{GameVersion.Parse(build).Revision}}, "lines": ["Change of {{build}}."] } ] }""";

    /// <summary>Answers the master server with 2026.9.10.5438, and serves the ksa-versions file that <paramref name="file"/> gives for a revision, or 404.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> WithVersions(Func<int, HttpResponseMessage?> file) => request =>
    {
        if (request.RequestUri is not { Host: VersionsHost } uri || !uri.AbsolutePath.StartsWith(VersionsPath, StringComparison.Ordinal))
            return MasterServer("2026.9.10.5438")(request);

        var revision = int.Parse(Path.GetFileNameWithoutExtension(uri.AbsolutePath).Split('.')[^1], CultureInfo.InvariantCulture);
        return file(revision) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
    };

    private static Func<HttpRequestMessage, HttpResponseMessage?> WithVersions(IReadOnlyDictionary<int, string> files) =>
        WithVersions(revision => files.TryGetValue(revision, out var json) ? Json(json) : null);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static List<string> VersionFilesRequested(ViewModelHarness harness) =>
        harness.Requests.Where(uri => uri.Host == VersionsHost && uri.AbsolutePath.StartsWith(VersionsPath, StringComparison.Ordinal)).Select(uri => uri.AbsolutePath[VersionsPath.Length..]).ToList();

    private static void WriteInstalledVersionFile(string game) =>
        WriteVersionFile(game, "v2026.8.X.5117.json", """{ "build": "2026.8.3.5117", "fromRevision": 5056, "toRevision": 5117, "commits": [ { "rev": 5117, "lines": ["Installed change."] } ] }""");

    private static async Task<MainViewModel> OpenPatchNotesAsync(ViewModelHarness harness)
    {
        await harness.ViewModel.OpenGamePatchNotesCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenNewerGamePatchNotesLoadedAsync();
        return harness.ViewModel;
    }

    /// <summary>Answers the master server with <paramref name="version"/> and <paramref name="url"/>, and fails every other request.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> MasterServer(string version, string url = "https://ksa.ahwoo.com") => request =>
        request.RequestUri?.Host != MasterServerHost
            ? null
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { Version = version, Url = url }), Encoding.UTF8, "application/json"),
            };

    /// <summary>A harness whose game folder holds a KSA.dll of <see cref="InstalledBuild"/>, after the master server check.</summary>
    private static async Task<ViewModelHarness> CreateAsync(Func<HttpRequestMessage, HttpResponseMessage?>? respond, AppPreferences? preferences = null, Action<string>? fillGame = null, Func<string, string>? editSnapshot = null)
    {
        var harness = await ViewModelHarness.CreateAsync(async services =>
        {
            var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
            File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
            fillGame?.Invoke(game);
            await services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            if (preferences is not null)
                await services.AppPreferences.SaveAsync(preferences, MainViewModel.BundledThemeNames);
        }, respond, editSnapshot);
        await harness.ViewModel.WhenGameBuildCheckedAsync();
        return harness;
    }

    private static void WriteVersionFile(string game, string name, string json)
    {
        var folder = Path.Combine(game, "Content", "Versions");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), json);
    }

    [Fact]
    public async Task Load_HigherRevision_ShowsTheBannerWithTheDownloadPage()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"));
        var viewModel = harness.ViewModel;

        Assert.Equal(InstalledBuild, viewModel.InstalledVersionText);
        Assert.True(viewModel.ShowGameBuildBanner);
        Assert.Equal("A newer KSA build is available: 2026.9.10.5438", viewModel.GameBuildBannerText);
        Assert.Equal("https://ksa.ahwoo.com/", viewModel.GameBuildDownloadUrl);
        Assert.Single(harness.Requests, uri => uri.Host == MasterServerHost);

        harness.Localization.TrySetCulture("de");
        Assert.Equal(harness.Localization.GameBuildAvailable + " 2026.9.10.5438", viewModel.GameBuildBannerText);
    }

    [Theory]
    [InlineData(InstalledBuild, false)]
    [InlineData("2026.7.1.5000", false)]
    [InlineData("2027.1.1.5117", false)]
    [InlineData("2025.1.1.5118", true)]
    public async Task Load_ComparesOnlyTheRevision(string latest, bool shown)
    {
        using var harness = await CreateAsync(MasterServer(latest));

        Assert.Equal(shown, harness.ViewModel.ShowGameBuildBanner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ksa.ahwoo.com")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    public async Task Load_NoWebAddressFromTheMasterServer_ShowsTheBannerWithoutALink(string url)
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438", url));
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowGameBuildBanner);
        Assert.Null(viewModel.GameBuildDownloadUrl);
    }

    [Fact]
    public async Task Load_MasterServerOffline_ShowsNoBannerAndNoError()
    {
        using var harness = await CreateAsync(respond: null);
        var viewModel = harness.ViewModel;

        Assert.Single(harness.Requests, uri => uri.Host == MasterServerHost);
        Assert.False(viewModel.ShowGameBuildBanner);
        Assert.Null(viewModel.GameBuildBannerText);
        Assert.Null(viewModel.UnexpectedError);
    }

    [Fact]
    public async Task ServicesRebuiltAfterNoAnswer_AsksTheMasterServerAgain()
    {
        var online = false;
        var answer = MasterServer("2026.9.10.5438");
        using var harness = await CreateAsync(request => online ? answer(request) : null);
        var viewModel = harness.ViewModel;
        Assert.False(viewModel.ShowGameBuildBanner);

        online = true;
        viewModel.GameDirectoryInput = Path.Combine(Path.GetDirectoryName(harness.Services.Paths.GetBoreaSettingsPath())!, "Game");
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        await viewModel.WhenGameBuildCheckedAsync();

        Assert.True(viewModel.ShowGameBuildBanner);
        Assert.Equal(2, harness.Requests.Count(uri => uri.Host == MasterServerHost));
    }

    [Fact]
    public async Task Load_UnreadableAnswer_ShowsNoBanner()
    {
        using var harness = await CreateAsync(MasterServer("not a version"));

        Assert.False(harness.ViewModel.ShowGameBuildBanner);
    }

    [Fact]
    public async Task Load_NoGameFolder_DoesNotAskTheMasterServer()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: MasterServer("2026.9.10.5438"));
        var viewModel = harness.ViewModel;
        await viewModel.WhenGameBuildCheckedAsync();

        Assert.Null(viewModel.InstalledVersionText);
        Assert.False(viewModel.ShowGameBuildBanner);
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == MasterServerHost);
    }

    [Fact]
    public async Task Load_UpdateCheckOff_DoesNotAskTheMasterServer()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"), AppPreferences.Empty.WithCheckForUpdatesAtStart(false));

        Assert.False(harness.ViewModel.ShowGameBuildBanner);
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == MasterServerHost);
    }

    [Fact]
    public async Task GameUpdatedWhileOpen_HidesTheBanner()
    {
        using var harness = await CreateAsync(MasterServer(InstalledBuild), fillGame: game =>
            File.Copy(Path.Combine(AppContext.BaseDirectory, "LoaderVersionFixture.dll"), Path.Combine(game, "KSA.dll"), overwrite: true));
        var viewModel = harness.ViewModel;
        Assert.True(viewModel.ShowGameBuildBanner);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(Path.GetDirectoryName(harness.Services.Paths.GetBoreaSettingsPath())!, "Game", "KSA.dll"), overwrite: true);
        await viewModel.RefreshInstalledGameAsync();

        Assert.Equal(InstalledBuild, viewModel.InstalledVersionText);
        Assert.False(viewModel.ShowGameBuildBanner);
        Assert.Single(harness.Requests, uri => uri.Host == MasterServerHost);
    }

    [Fact]
    public async Task Close_HidesTheBannerAndSavesTheRevision()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"));
        var viewModel = harness.ViewModel;

        viewModel.DismissGameBuildBannerCommand.Execute(null);
        await viewModel.WhenPreferencesSavedAsync();

        Assert.False(viewModel.ShowGameBuildBanner);
        Assert.Null(viewModel.PreferenceSaveError);
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(5438, saved.Preferences.DismissedGameRevision);
    }

    [Theory]
    [InlineData(5438, false)]
    [InlineData(5500, false)]
    [InlineData(5402, true)]
    public async Task Load_ClosedBefore_ShowsTheBannerOnlyForAHigherRevision(int dismissed, bool shown)
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"), AppPreferences.Empty.WithDismissedGameRevision(dismissed));

        Assert.Equal(shown, harness.ViewModel.ShowGameBuildBanner);
    }

    [Fact]
    public async Task SeeMore_ListsTheInstalledPatchNotesNewestFirst()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"), fillGame: game =>
        {
            WriteVersionFile(game, "v2026.8.X.5117.json", """{ "build": "2026.8.3.5117", "date": "2026-08-20", "fromRevision": 5100, "toRevision": 5117, "commits": [ { "rev": 5117, "lines": ["Newest change."] } ] }""");
            WriteVersionFile(game, "v2026.7.X.5100.json", """{ "build": "2026.7.6.5100", "fromRevision": 5000, "toRevision": 5100, "commits": [ { "rev": 5099, "lines": ["Older change."] } ] }""");
            WriteVersionFile(game, "broken.json", """{ "build": "2026.8.4.5200", "commits": """);
        });
        var viewModel = await OpenPatchNotesAsync(harness);
        var installed = viewModel.GamePatchNotes.Where(item => item.IsInstalled).ToList();

        Assert.True(viewModel.IsGamePatchNotesOpen);
        Assert.False(viewModel.HasNoGamePatchNotes);
        Assert.Equal(["2026.8.3.5117", "2026.7.6.5100"], installed.Select(item => item.Build));
        Assert.Equal(["Newest change."], installed[0].Lines);
        Assert.NotNull(installed[0].DateText);
        Assert.Null(installed[1].DateText);

        viewModel.CloseGamePatchNotesCommand.Execute(null);

        Assert.False(viewModel.IsGamePatchNotesOpen);
        Assert.True(viewModel.ShowGameBuildBanner);
    }

    [Fact]
    public async Task Download_OpensThePageOfTheMasterServer()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"));
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;

        viewModel.OpenGameBuildDownloadCommand.Execute(null);

        Assert.Equal(["https://ksa.ahwoo.com/"], opened);
        Assert.Null(viewModel.GamePatchNotesError);
    }

    [Fact]
    public async Task Download_CannotOpen_ShowsTheErrorInThePatchNotes()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"));
        var viewModel = harness.ViewModel;
        viewModel.OpenWithSystem = _ => throw new Win32Exception("No browser is set.");
        await viewModel.OpenGamePatchNotesCommand.ExecuteAsync(null);

        viewModel.OpenGameBuildDownloadCommand.Execute(null);

        Assert.Equal("No browser is set.", viewModel.GamePatchNotesError);
        Assert.Null(viewModel.AboutError);
    }

    [Fact]
    public async Task SeeMore_NoVersionsFolder_ShowsTheEmptyText()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"));
        var viewModel = harness.ViewModel;

        await viewModel.OpenGamePatchNotesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGamePatchNotesOpen);
        Assert.True(viewModel.HasNoGamePatchNotes);
    }

    [Fact]
    public async Task SeeMore_NewerBuilds_ListsTheirNotesFirstWithTheBuildsTheirFilesName()
    {
        using var harness = await CreateAsync(WithVersions(PublishedFiles), fillGame: WriteInstalledVersionFile);

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.Equal(["2026.9.10.5438", "2026.9.7.5402", "2026.9.4.5400", "2026.8.22.5348", "2026.8.19.5261", "2026.8.5.5168", "2026.8.3.5117"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.Equal([false, false, false, false, false, false, true], viewModel.GamePatchNotes.Select(item => item.IsInstalled));
        Assert.Equal(["Change of 2026.9.10.5438."], viewModel.GamePatchNotes[0].Lines);
        Assert.Equal(["v2026.9.X.5438.json", "v2026.9.X.5402.json", "v2026.9.X.5400.json", "v2026.8.X.5348.json", "v2026.8.X.5261.json", "v2026.8.X.5168.json"], VersionFilesRequested(harness));
        Assert.False(viewModel.IsLoadingNewerGamePatchNotes);
        Assert.False(viewModel.NewerGamePatchNotesFailed);
        Assert.False(viewModel.NewerGamePatchNotesCapped);
    }

    [Fact]
    public async Task SeeMore_Again_ReadsTheDownloadedFilesFromTheDataFolder()
    {
        using var harness = await CreateAsync(WithVersions(PublishedFiles), fillGame: WriteInstalledVersionFile);
        var viewModel = await OpenPatchNotesAsync(harness);
        viewModel.CloseGamePatchNotesCommand.Execute(null);

        await OpenPatchNotesAsync(harness);

        Assert.Equal(7, viewModel.GamePatchNotes.Count);
        Assert.Equal(6, VersionFilesRequested(harness).Count);
        Assert.True(File.Exists(Path.Combine(harness.Root, "GamePatchNotes", "v2026.9.X.5438.json")));
    }

    [Fact]
    public async Task SeeMore_NewestFileMissing_ShowsTheOthersAndTheLine()
    {
        using var harness = await CreateAsync(WithVersions(PublishedFiles.Where(file => file.Key != 5438).ToDictionary()), fillGame: WriteInstalledVersionFile);

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.Equal(["2026.9.7.5402", "2026.9.4.5400", "2026.8.22.5348", "2026.8.19.5261", "2026.8.5.5168", "2026.8.3.5117"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.True(viewModel.NewerGamePatchNotesFailed);
        Assert.Null(viewModel.UnexpectedError);
    }

    [Fact]
    public async Task SeeMore_Timeout_KeepsTheInstalledNotesAndShowsTheLine()
    {
        using var harness = await CreateAsync(WithVersions(_ => throw new TaskCanceledException("The request timed out.", new TimeoutException())), fillGame: WriteInstalledVersionFile);

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.Equal(["2026.8.3.5117"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.True(viewModel.NewerGamePatchNotesFailed);
        Assert.False(viewModel.IsLoadingNewerGamePatchNotes);
        Assert.Single(VersionFilesRequested(harness));
        Assert.Null(viewModel.UnexpectedError);
    }

    [Fact]
    public async Task SeeMore_FileAboveTheCap_IsLeftOutWithTheLine()
    {
        var files = new Dictionary<int, string>(PublishedFiles) { [5402] = PublishedFiles[5402] + new string(' ', GamePatchNotesFile.MaxDownloadBytes) };
        using var harness = await CreateAsync(WithVersions(files), fillGame: WriteInstalledVersionFile);

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.DoesNotContain(viewModel.GamePatchNotes, item => item.Build == "2026.9.7.5402");
        Assert.Equal(5, viewModel.GamePatchNotes.Count);
        Assert.True(viewModel.NewerGamePatchNotesFailed);
        Assert.False(File.Exists(Path.Combine(harness.Root, "GamePatchNotes", "v2026.9.X.5402.json")));
    }

    [Fact]
    public async Task SeeMore_BrokenFile_IsSkippedWithoutTheLine()
    {
        var files = new Dictionary<int, string>(PublishedFiles) { [5348] = """{ "build": "2026.8.22.5348", "commits": """ };
        using var harness = await CreateAsync(WithVersions(files), fillGame: WriteInstalledVersionFile);

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.Equal(["2026.9.10.5438", "2026.9.7.5402", "2026.9.4.5400", "2026.8.19.5261", "2026.8.5.5168", "2026.8.3.5117"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.False(viewModel.NewerGamePatchNotesFailed);
    }

    [Fact]
    public async Task SeeMore_BrokenFileInTheDataFolder_IsDownloadedAgain()
    {
        using var harness = await CreateAsync(WithVersions(PublishedFiles), fillGame: WriteInstalledVersionFile);
        var cached = Path.Combine(harness.Root, "GamePatchNotes", "v2026.9.X.5438.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        await File.WriteAllTextAsync(cached, """{ "build": "2026.9.10.5438", "commits": """);

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.Equal("2026.9.10.5438", viewModel.GamePatchNotes[0].Build);
        Assert.Contains("v2026.9.X.5438.json", VersionFilesRequested(harness));
        Assert.Equal(PublishedFiles[5438], await File.ReadAllTextAsync(cached));
    }

    [Fact]
    public async Task SeeMore_UnexpectedFailure_KeepsTheInstalledNotesAndShowsTheLine()
    {
        using var harness = await CreateAsync(WithVersions(_ => throw new InvalidOperationException("Unexpected.")), fillGame: WriteInstalledVersionFile);

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.Equal(["2026.8.3.5117"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.True(viewModel.NewerGamePatchNotesFailed);
        Assert.False(viewModel.IsLoadingNewerGamePatchNotes);
        Assert.Null(viewModel.UnexpectedError);
        Assert.Contains(harness.Services.Log.ReadRecentLines(20), line => line.Contains("Unexpected.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeeMore_MoreThanTwentyNewerBuilds_LoadsTheTwentyNewestAndSaysSo()
    {
        var added = Enumerable.Range(5500, 25).Select(revision => $"\"2026.9.20.{revision}\"");
        using var harness = await CreateAsync(
            WithVersions(revision => Json(ChangeLog($"2026.9.20.{revision}", revision - 1))),
            fillGame: WriteInstalledVersionFile,
            editSnapshot: json => json.Replace("\"2026.9.7.5402\"", "\"2026.9.7.5402\", " + string.Join(", ", added), StringComparison.Ordinal));

        var viewModel = await OpenPatchNotesAsync(harness);

        Assert.Equal(MainViewModel.MaxNewerGamePatchNotes, VersionFilesRequested(harness).Count);
        Assert.Equal("v2026.9.X.5524.json", VersionFilesRequested(harness)[0]);
        Assert.Equal(MainViewModel.MaxNewerGamePatchNotes + 1, viewModel.GamePatchNotes.Count);
        Assert.Equal("2026.9.20.5524", viewModel.GamePatchNotes[0].Build);
        Assert.True(viewModel.NewerGamePatchNotesCapped);
        Assert.Equal("Only the 20 newest builds are shown.", viewModel.NewerGamePatchNotesCappedText);
    }

    [Fact]
    public async Task Close_WhileTheNewerNotesLoad_CancelsTheRequest()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stalled = new StalledContent(reading);
        using var harness = await CreateAsync(WithVersions(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = stalled }), fillGame: WriteInstalledVersionFile);
        var viewModel = harness.ViewModel;
        await viewModel.OpenGamePatchNotesCommand.ExecuteAsync(null);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(viewModel.IsLoadingNewerGamePatchNotes);

        viewModel.CloseGamePatchNotesCommand.Execute(null);
        await viewModel.WhenNewerGamePatchNotesLoadedAsync();

        Assert.True(stalled.Canceled);
        Assert.False(viewModel.IsLoadingNewerGamePatchNotes);
        Assert.False(viewModel.NewerGamePatchNotesFailed);
        Assert.Equal(["2026.8.3.5117"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.Single(VersionFilesRequested(harness));
    }

    [Fact]
    public async Task SeeMore_AgainWhileTheNewerNotesLoad_CancelsTheFirstLoad()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stalled = new StalledContent(reading);
        var stall = 1;
        using var harness = await CreateAsync(
            WithVersions(revision => Interlocked.Exchange(ref stall, 0) == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = stalled }
                : PublishedFiles.TryGetValue(revision, out var json) ? Json(json) : null),
            fillGame: WriteInstalledVersionFile);
        var viewModel = harness.ViewModel;
        await viewModel.OpenGamePatchNotesCommand.ExecuteAsync(null);
        var firstLoad = viewModel.WhenNewerGamePatchNotesLoadedAsync();
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await OpenPatchNotesAsync(harness);
        await firstLoad;

        Assert.True(stalled.Canceled);
        Assert.Equal(["2026.9.10.5438", "2026.9.7.5402", "2026.9.4.5400", "2026.8.22.5348", "2026.8.19.5261", "2026.8.5.5168", "2026.8.3.5117"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.False(viewModel.NewerGamePatchNotesFailed);
        Assert.False(viewModel.IsLoadingNewerGamePatchNotes);
    }

    /// <summary>A response body that sends nothing until its read is canceled.</summary>
    private sealed class StalledContent(TaskCompletionSource reading) : HttpContent
    {
        public bool Canceled { get; private set; }

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            reading.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Canceled = true;
                throw;
            }

            return Stream.Null;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
