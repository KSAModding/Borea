using System.Net;
using System.Text;
using System.Text.Json;
using Borea.App.Formatting;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.History;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using Borea.Core.Updates;

namespace Borea.App.Tests.ViewModels;

public sealed class UpdateNoticeViewModelTests
{
    private const string ReleaseHost = "api.github.com";

    private const string ReleasesPath = "/repos/KSAModding/Borea/releases";

    private static string Release(string tag, bool prerelease = false, string body = "") =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/KSAModding/Borea/releases/tag/{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"published_at":"2026-09-13T12:00:00Z","body":{{JsonSerializer.Serialize(body)}}}""";

    /// <summary>Answers the release check with a releases list that holds <paramref name="releases"/>.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> Releases(params string[] releases) => request =>
        request.RequestUri?.Host != ReleaseHost
            ? null
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[" + string.Join(",", releases) + "]", Encoding.UTF8, "application/json"),
            };

    private static Func<HttpRequestMessage, HttpResponseMessage?> ReleaseAt(string tag) => Releases(Release(tag));

    /// <summary>Writes the preferences file of a Borea before 0.2.0 that kept a closed update banner closed.</summary>
    private static Func<BoreaServices, Task> ClosedBeforeFor(string version) =>
        services => File.WriteAllTextAsync(services.Paths.GetAppPreferencesPath(), $$"""{ "formatVersion": 1, "dismissedBoreaRelease": "{{version}}" }""");

    [Fact]
    public async Task Load_NewerRelease_ShowsTheNoticeAndTheBanner()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.True(viewModel.HasAvailableUpdate);
        Assert.True(viewModel.ShowReleaseBanner);
        Assert.Equal("999.0.0", viewModel.AvailableUpdateVersion);
        Assert.Equal("A newer Borea release is available: 999.0.0", viewModel.AvailableUpdateText);
        Assert.Equal("https://github.com/KSAModding/Borea/releases/tag/v999.0.0", viewModel.AvailableUpdateUrl);
        var request = Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
        Assert.Equal(ReleasesPath, request.AbsolutePath);

        harness.Localization.TrySetCulture("de");
        Assert.StartsWith("Eine neuere Borea-Version", harness.Localization.UpdateAvailable, StringComparison.Ordinal);
        Assert.Equal(harness.Localization.UpdateAvailable + " 999.0.0", viewModel.AvailableUpdateText);
    }

    [Fact]
    public async Task Load_ABuildThatIsNoReleaseArchive_ShowsTheReleaseAndWhyItCannotUpdate()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        // the tests run from loose assemblies, which is not what a release archive holds
        Assert.Null(viewModel.SelfUpdateActionText);
        Assert.Equal($"{viewModel.AvailableUpdateText} {harness.Localization.SelfUpdateNotAReleaseBuild}", viewModel.ReleaseBannerText);
    }

    [Fact]
    public async Task SelfUpdate_ABuildThatCannotReplaceItself_SaysSoInTheBanner()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();

        await viewModel.SelfUpdateCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.SelfUpdateNotAReleaseBuild, viewModel.SelfUpdateStatus);
        Assert.Equal(harness.Localization.SelfUpdateNotAReleaseBuild, viewModel.ReleaseBannerText);
        Assert.False(viewModel.IsSelfUpdating);
    }

    [Fact]
    public async Task Load_ReleaseOfTheRunningVersion_ShowsNoNotice()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v" + MainViewModel.BoreaVersion));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.False(viewModel.HasAvailableUpdate);
        Assert.False(viewModel.ShowReleaseBanner);
        Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
    }

    [Fact]
    public async Task Load_NoPublishedRelease_ShowsNothingAndNoError()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: request => request.RequestUri?.Host != ReleaseHost
            ? null
            : new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"message":"Not Found","status":"404"}""", Encoding.UTF8, "application/json"),
            });
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.False(viewModel.HasAvailableUpdate);
        Assert.Null(viewModel.UnexpectedError);
    }

    [Fact]
    public async Task Load_Offline_ShowsNothingAndNoError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.False(viewModel.HasAvailableUpdate);
        Assert.Null(viewModel.UnexpectedError);
        Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
    }

    [Fact]
    public async Task Load_PreferenceOff_SendsNoRequest()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithCheckForUpdatesAtStart(false), MainViewModel.BundledThemeNames),
            ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.False(viewModel.CheckForUpdatesAtStart);
        Assert.False(viewModel.HasAvailableUpdate);
        Assert.NotEmpty(harness.Requests);
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == ReleaseHost);
    }

    [Fact]
    public async Task Load_AgainInTheSameStart_DoesNotAskAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;

        await viewModel.LoadAsync();
        await viewModel.WhenUpdateCheckedAsync();

        Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
    }

    [Fact]
    public async Task Load_StableChannel_DoesNotReportAPreRelease()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Releases(Release("v999.0.0-beta.1", prerelease: true), Release("v" + MainViewModel.BoreaVersion)));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.Equal(BoreaUpdateChannel.Stable, viewModel.SelectedUpdateChannel.Channel);
        Assert.False(viewModel.HasAvailableUpdate);
        var request = Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
        Assert.Equal(ReleasesPath, request.AbsolutePath);
    }

    [Fact]
    public async Task Load_TestingChannel_ReportsTheNewerBetaWithOneRequest()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithUpdateChannel(BoreaUpdateChannel.Testing), MainViewModel.BundledThemeNames),
            Releases(Release("v999.0.0-beta.1", prerelease: true), Release("v" + MainViewModel.BoreaVersion)));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.Equal(BoreaUpdateChannel.Testing, viewModel.SelectedUpdateChannel.Channel);
        Assert.True(viewModel.HasAvailableUpdate);
        Assert.Equal("999.0.0-beta.1", viewModel.AvailableUpdateVersion);
        var request = Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
        Assert.Equal(ReleasesPath, request.AbsolutePath);
    }

    [Fact]
    public async Task ChooseUpdateChannel_AfterTheCheck_SavesThePreferenceAndSendsNoSecondRequest()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Releases(Release("v999.0.0-beta.1", prerelease: true), Release("v" + MainViewModel.BoreaVersion)));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();

        viewModel.SelectedUpdateChannel = viewModel.UpdateChannelOptions.Single(option => option.Channel == BoreaUpdateChannel.Testing);
        await viewModel.WhenPreferencesSavedAsync();
        await viewModel.WhenUpdateCheckedAsync();

        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(BoreaUpdateChannel.Testing, saved.Preferences.UpdateChannel);
        Assert.Equal(BoreaUpdateChannel.Testing, viewModel.SelectedUpdateChannel.Channel);
        Assert.Null(viewModel.PreferenceSaveError);
        Assert.False(viewModel.HasAvailableUpdate);
        Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
    }

    [Fact]
    public async Task SeeMore_ListsOnlyTheNewerReleasesNewestFirstAndMarksTheInstalledOne()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Releases(
            Release("v998.0.0"),
            Release("v0.0.1", body: "Old notes"),
            Release("v999.0.0", body: "## Faster installs"),
            Release("v" + MainViewModel.BoreaVersion, body: "Installed notes")));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();

        viewModel.OpenReleaseNotesCommand.Execute(null);

        Assert.True(viewModel.IsReleaseNotesOpen);
        Assert.Equal(["999.0.0", "998.0.0", MainViewModel.BoreaVersion], viewModel.ReleaseNotes.Select(item => item.Version));
        Assert.Equal([false, false, true], viewModel.ReleaseNotes.Select(item => item.IsInstalled));
        var newest = viewModel.ReleaseNotes[0];
        Assert.Equal("## Faster installs", newest.Notes);
        Assert.False(newest.HasNoNotes);
        Assert.Equal("https://github.com/KSAModding/Borea/releases/tag/v999.0.0", newest.PageUrl);
        Assert.NotNull(newest.DateText);
        Assert.True(viewModel.ReleaseNotes[1].HasNoNotes);
        Assert.Equal("Installed notes", viewModel.ReleaseNotes[2].Notes);
        Assert.Null(viewModel.ReleaseNotes[2].PageUrl);

        viewModel.CloseReleaseNotesCommand.Execute(null);

        Assert.False(viewModel.IsReleaseNotesOpen);
        Assert.True(viewModel.ShowReleaseBanner);
    }

    [Fact]
    public async Task SeeMore_InstalledReleaseNotInTheList_StillMarksTheInstalledVersion()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithUpdateChannel(BoreaUpdateChannel.Dev), MainViewModel.BundledThemeNames),
            Releases(Release("v999.0.0-dev.1", prerelease: true)));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();

        viewModel.OpenReleaseNotesCommand.Execute(null);

        Assert.Equal(["999.0.0-dev.1", MainViewModel.BoreaVersion], viewModel.ReleaseNotes.Select(item => item.Version));
        Assert.True(viewModel.ReleaseNotes[0].IsPreRelease);
        var installed = viewModel.ReleaseNotes[1];
        Assert.True(installed.IsInstalled);
        Assert.Null(installed.Notes);
        Assert.False(installed.HasNoNotes);
        Assert.Null(installed.DateText);
    }

    [Fact]
    public async Task Close_HidesTheBannerAndKeepsTheNotice()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();

        viewModel.DismissReleaseBannerCommand.Execute(null);

        Assert.False(viewModel.ShowReleaseBanner);
        Assert.True(viewModel.HasAvailableUpdate);
    }

    [Fact]
    public async Task Load_AReleaseClosedByAnOlderBorea_ShowsTheBannerAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync(ClosedBeforeFor("999.0.0"), ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.True(viewModel.ShowReleaseBanner);
    }

    [Fact]
    public async Task ChangeLanguage_RefreshesTheUpdateChannelTexts()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var option = viewModel.UpdateChannelOptions.Single(item => item.Channel == BoreaUpdateChannel.Testing);
        var raised = false;
        option.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(BoreaUpdateChannelOption.Text);

        harness.Localization.SelectedCulture = harness.Localization.SupportedCultures.Single(culture => culture.Name == "de");
        await viewModel.WhenPreferencesSavedAsync();

        Assert.True(raised);
        Assert.Equal("Stabile und Beta-Versionen", option.Text);
    }

    [Fact]
    public async Task TurnSwitchOff_SavesThePreference()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        Assert.True(viewModel.CheckForUpdatesAtStart);

        viewModel.CheckForUpdatesAtStart = false;
        await viewModel.WhenPreferencesSavedAsync();

        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.False(saved.Preferences.CheckForUpdatesAtStart);
        Assert.False(viewModel.CheckForUpdatesAtStart);
        Assert.Null(viewModel.PreferenceSaveError);
    }

    [Fact]
    public async Task SelfUpdate_ACloseWhileTheNewBuildTakesItsPlace_WaitsUntilItIsInPlace()
    {
        var install = new TaskCompletionSource();
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"), selfUpdater: new WaitingSelfUpdater(install.Task));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();
        await viewModel.Tasks.WhenSavedAsync();
        var closed = false;

        // nothing holds the window before the update starts
        Assert.True(viewModel.RequestClose(() => closed = true));

        var update = viewModel.SelfUpdateCommand.ExecuteAsync(null);
        await ViewModelHarness.WaitUntilAsync(() => viewModel.IsInstallingSelfUpdate);

        Assert.False(viewModel.RequestClose(() => closed = true));
        Assert.False(closed);

        install.SetResult();
        await update;
        await ViewModelHarness.WaitUntilAsync(() => closed);
    }

    [Fact]
    public async Task SelfUpdate_WhileTheNewBuildTakesItsPlace_OffersNoCloseNow_AndDisablesAnOpenOne()
    {
        var install = new TaskCompletionSource();
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"), selfUpdater: new WaitingSelfUpdater(install.Task));
        var viewModel = harness.ViewModel;
        await viewModel.WhenUpdateCheckedAsync();
        var closeNowChanges = 0;
        viewModel.CloseNowCommand.CanExecuteChanged += (_, _) => closeNowChanges++;
        Assert.True(viewModel.CloseNowCommand.CanExecute(null));

        var update = viewModel.SelfUpdateCommand.ExecuteAsync(null);
        await ViewModelHarness.WaitUntilAsync(() => viewModel.IsInstallingSelfUpdate);
        Assert.False(viewModel.RequestClose(() => { }));
        Assert.False(viewModel.RequestClose(() => { }));

        Assert.False(viewModel.IsCloseNowOpen);
        Assert.False(viewModel.CloseNowCommand.CanExecute(null));
        Assert.Equal(1, closeNowChanges);

        install.SetResult();
        await update;
        Assert.True(viewModel.CloseNowCommand.CanExecute(null));
        Assert.Equal(2, closeNowChanges);
    }

    [Fact]
    public async Task SelfUpdate_RunsAsATask_ThatShowsTheDownloadTheCheckAndTheUnpackingInTurn()
    {
        var updater = new SteppingSelfUpdater();
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"), selfUpdater: updater);
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await viewModel.WhenUpdateCheckedAsync();
        await viewModel.Tasks.WhenSavedAsync();

        var update = viewModel.SelfUpdateCommand.ExecuteAsync(null);
        var task = Assert.Single(viewModel.Tasks.Running);
        Assert.Equal(localization.FormatTaskSelfUpdate("999.0.0"), task.Title);

        await ViewModelHarness.WaitUntilAsync(() => task.Progress == 40);
        Assert.Equal(localization.FormatInstallDownloading("Borea 999.0.0"), task.Step);
        Assert.True(task.HasProgress);
        Assert.True(viewModel.Tasks.HasProgress);
        updater.Next();

        await ViewModelHarness.WaitUntilAsync(() => task.Step == localization.FormatSelfUpdateVerifying("999.0.0"));
        Assert.False(task.HasProgress);
        updater.Next();

        await ViewModelHarness.WaitUntilAsync(() => task.Step == localization.FormatSelfUpdateUnpacking("999.0.0"));
        updater.Next();
        await update;

        Assert.Empty(viewModel.Tasks.Running);
        Assert.Same(task, viewModel.Tasks.History[0]);
        Assert.Equal(TaskState.Finished, task.State);
        Assert.Equal(localization.FormatSelfUpdateStartAgain("999.0.0"), viewModel.ReleaseBannerText);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Theory]
    [InlineData(SelfUpdateFailure.Download)]
    [InlineData(SelfUpdateFailure.Checksum)]
    public async Task SelfUpdate_AFailedDownloadOrCheck_EndsTheTaskAsFailedWithTheReason_AndTheHistoryKeepsIt(SelfUpdateFailure failure)
    {
        var updater = new SteppingSelfUpdater(failure);
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"), selfUpdater: updater);
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await viewModel.WhenUpdateCheckedAsync();

        var update = viewModel.SelfUpdateCommand.ExecuteAsync(null);
        var task = Assert.Single(viewModel.Tasks.Running);
        await ViewModelHarness.WaitUntilAsync(() => task.Progress == 40);
        updater.Next();
        if (failure == SelfUpdateFailure.Checksum)
        {
            await ViewModelHarness.WaitUntilAsync(() => task.Step == localization.FormatSelfUpdateVerifying("999.0.0"));
            updater.Next();
        }

        await update;

        var reason = failure == SelfUpdateFailure.Download ? localization.SelfUpdateDownloadFailed : localization.SelfUpdateChecksumFailed;
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(reason, task.FailureReason);
        Assert.Equal(reason, viewModel.ReleaseBannerText);
        Assert.Equal(localization.FormatToastSelfUpdateFailed("999.0.0"), Assert.Single(viewModel.Toasts.Items).Message);

        await viewModel.Tasks.WhenSavedAsync();
        var restarted = new MainViewModel(localization, new RegionalFormatService(localization), appPreferencesRepository: null, AppPreferences.Empty, harness.Services);
        await restarted.Tasks.LoadAsync();
        var entry = Assert.Single(restarted.Tasks.History, item => item.Kind == TaskKind.BoreaUpdate);
        Assert.Equal(task.Title, entry.Title);
        Assert.Equal(TaskState.Failed, entry.State);
        Assert.Equal(reason, entry.FailureReason);
        Assert.False(entry.HasRetry);
    }

    private static StagedSelfUpdate Staged(BoreaRelease release, Action install)
    {
        var folder = Path.Combine(Path.GetTempPath(), "BoreaSelfUpdateTest");
        var program = Path.Combine(folder, "borea.exe");
        return new StagedSelfUpdate(release.Version, folder, program, program + ".old", install, () => { });
    }

    /// <summary>Holds the update in the step where the new build takes the place of the running one.</summary>
    private sealed class WaitingSelfUpdater(Task install) : ISelfUpdater
    {
        public SelfUpdateReadiness GetReadiness() => SelfUpdateReadiness.Ready;

        public Task<StagedSelfUpdate> StageAsync(BoreaRelease release, IProgress<SelfUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Staged(release, install.GetAwaiter().GetResult));
    }

    /// <summary>Reports the download, the check and the unpacking, each held until <see cref="Next"/>, and fails after the step of its failure.</summary>
    private sealed class SteppingSelfUpdater(SelfUpdateFailure? failure = null) : ISelfUpdater
    {
        private readonly SemaphoreSlim _next = new(0);

        public void Next() => _next.Release();

        public SelfUpdateReadiness GetReadiness() => SelfUpdateReadiness.Ready;

        public async Task<StagedSelfUpdate> StageAsync(BoreaRelease release, IProgress<SelfUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            (SelfUpdateProgress Report, SelfUpdateFailure Failure)[] steps =
            [
                (new(SelfUpdatePhase.Downloading, 40, 100), SelfUpdateFailure.Download),
                (new(SelfUpdatePhase.Verifying), SelfUpdateFailure.Checksum),
                (new(SelfUpdatePhase.Unpacking), SelfUpdateFailure.Unpack),
            ];
            foreach (var (report, stepFailure) in steps)
            {
                progress?.Report(report);
                await _next.WaitAsync(cancellationToken);
                if (failure == stepFailure)
                    throw new SelfUpdateFailedException(stepFailure, "The test fails this step.");
            }

            return Staged(release, () => { });
        }
    }
}
