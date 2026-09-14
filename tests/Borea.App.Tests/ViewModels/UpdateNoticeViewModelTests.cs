using System.Net;
using System.Text;
using Borea.App.ViewModels;
using Borea.Core.Preferences;
using Borea.Core.Updates;

namespace Borea.App.Tests.ViewModels;

public sealed class UpdateNoticeViewModelTests
{
    private const string ReleaseHost = "api.github.com";

    /// <summary>Answers the release check with a fixture release at <paramref name="tag"/>.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> ReleaseAt(string tag) => request =>
        request.RequestUri?.Host != ReleaseHost
            ? null
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/KSAModding/Borea/releases/tag/{{tag}}","draft":false,"prerelease":false}""",
                    Encoding.UTF8,
                    "application/json"),
            };

    /// <summary>Answers the latest release with <paramref name="latestTag"/> and the releases list with both tags.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> StableAndPreRelease(string latestTag, string preReleaseTag) => request =>
    {
        if (request.RequestUri?.Host != ReleaseHost)
            return null;

        static string Release(string tag, bool prerelease) =>
            $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/KSAModding/Borea/releases/tag/{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}}}""";

        var body = request.RequestUri.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)
            ? Release(latestTag, prerelease: false)
            : "[" + Release(preReleaseTag, prerelease: true) + "," + Release(latestTag, prerelease: false) + "]";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    };

    [Fact]
    public async Task Load_NewerRelease_ShowsTheNoticeWithVersionAndPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v999.0.0"));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.True(viewModel.HasAvailableUpdate);
        Assert.Equal("999.0.0", viewModel.AvailableUpdateVersion);
        Assert.Equal("https://github.com/KSAModding/Borea/releases/tag/v999.0.0", viewModel.AvailableUpdateUrl);
        Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
    }

    [Fact]
    public async Task Load_ReleaseOfTheRunningVersion_ShowsNoNotice()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ReleaseAt("v" + MainViewModel.BoreaVersion));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.False(viewModel.HasAvailableUpdate);
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
        using var harness = await ViewModelHarness.CreateAsync(respond: StableAndPreRelease("v" + MainViewModel.BoreaVersion, "v999.0.0-beta.1"));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.Equal(BoreaUpdateChannel.Stable, viewModel.SelectedUpdateChannel.Channel);
        Assert.False(viewModel.HasAvailableUpdate);
        Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost && uri.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Load_TestingChannel_ReportsTheNewerBetaAsAPreReleaseWithOneRequest()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithUpdateChannel(BoreaUpdateChannel.Testing), MainViewModel.BundledThemeNames),
            StableAndPreRelease("v" + MainViewModel.BoreaVersion, "v999.0.0-beta.1"));
        var viewModel = harness.ViewModel;

        await viewModel.WhenUpdateCheckedAsync();

        Assert.Equal(BoreaUpdateChannel.Testing, viewModel.SelectedUpdateChannel.Channel);
        Assert.True(viewModel.HasAvailableUpdate);
        Assert.Equal("999.0.0-beta.1", viewModel.AvailableUpdateVersion);
        Assert.True(viewModel.AvailableUpdateIsPreRelease);
        var request = Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
        Assert.Equal("/repos/KSAModding/Borea/releases", request.AbsolutePath);
    }

    [Fact]
    public async Task ChooseUpdateChannel_AfterTheCheck_SavesThePreferenceAndSendsNoSecondRequest()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: StableAndPreRelease("v" + MainViewModel.BoreaVersion, "v999.0.0-beta.1"));
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
        var request = Assert.Single(harness.Requests, uri => uri.Host == ReleaseHost);
        Assert.EndsWith("/releases/latest", request.AbsolutePath, StringComparison.Ordinal);
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
}
