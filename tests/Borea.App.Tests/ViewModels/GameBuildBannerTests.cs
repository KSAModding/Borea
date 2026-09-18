using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Borea.App.ViewModels;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class GameBuildBannerTests
{
    private const string MasterServerHost = "ksa-master1.rocketwerkz.com";

    private const string InstalledBuild = "2026.8.3.5117";

    /// <summary>Answers the master server with <paramref name="version"/> and <paramref name="url"/>, and fails every other request.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> MasterServer(string version, string url = "https://ksa.ahwoo.com") => request =>
        request.RequestUri?.Host != MasterServerHost
            ? null
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { Version = version, Url = url }), Encoding.UTF8, "application/json"),
            };

    /// <summary>A harness whose game folder holds a KSA.dll of <see cref="InstalledBuild"/>, after the master server check.</summary>
    private static async Task<ViewModelHarness> CreateAsync(Func<HttpRequestMessage, HttpResponseMessage?>? respond, AppPreferences? preferences = null, Action<string>? fillGame = null)
    {
        var harness = await ViewModelHarness.CreateAsync(async services =>
        {
            var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
            File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
            fillGame?.Invoke(game);
            await services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            if (preferences is not null)
                await services.AppPreferences.SaveAsync(preferences, MainViewModel.BundledThemeNames);
        }, respond);
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
    public async Task SeeMore_ListsTheInstalledPatchNotesNewestFirstWithoutARequest()
    {
        using var harness = await CreateAsync(MasterServer("2026.9.10.5438"), fillGame: game =>
        {
            WriteVersionFile(game, "v2026.8.X.5117.json", """{ "build": "2026.8.3.5117", "date": "2026-08-20", "fromRevision": 5100, "toRevision": 5117, "commits": [ { "rev": 5117, "lines": ["Newest change."] } ] }""");
            WriteVersionFile(game, "v2026.7.X.5100.json", """{ "build": "2026.7.6.5100", "fromRevision": 5000, "toRevision": 5100, "commits": [ { "rev": 5099, "lines": ["Older change."] } ] }""");
            WriteVersionFile(game, "broken.json", """{ "build": "2026.8.4.5200", "commits": """);
        });
        var viewModel = harness.ViewModel;
        var requests = harness.Requests.Count;

        await viewModel.OpenGamePatchNotesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGamePatchNotesOpen);
        Assert.False(viewModel.HasNoGamePatchNotes);
        Assert.Equal(["2026.8.3.5117", "2026.7.6.5100"], viewModel.GamePatchNotes.Select(item => item.Build));
        Assert.Equal(["Newest change."], viewModel.GamePatchNotes[0].Lines);
        Assert.NotNull(viewModel.GamePatchNotes[0].DateText);
        Assert.Null(viewModel.GamePatchNotes[1].DateText);
        Assert.Equal(requests, harness.Requests.Count);

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
}
