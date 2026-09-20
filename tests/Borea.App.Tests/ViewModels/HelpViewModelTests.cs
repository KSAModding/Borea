using System.Globalization;
using Borea.App.Localization;
using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class HelpViewModelTests
{
    [Fact]
    public async Task ShowHelp_SelectsTheTabAndClearsOldMessages()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.AboutMessage = "old";
        viewModel.AboutError = "old";

        viewModel.ShowHelpSettingsCommand.Execute(null);

        Assert.True(viewModel.IsHelpTab);
        Assert.False(viewModel.IsAboutTab);
        Assert.False(viewModel.IsGeneralTab);
        Assert.False(viewModel.IsGameTab);
        Assert.Null(viewModel.AboutMessage);
        Assert.Null(viewModel.AboutError);

        viewModel.ShowAboutSettingsCommand.Execute(null);
        Assert.True(viewModel.IsAboutTab);
        Assert.False(viewModel.IsHelpTab);
    }

    [Fact]
    public void Links_AreAbsoluteUrlsUnderTheProjectHosts()
    {
        string[] links = [MainViewModel.DiscordUrl, MainViewModel.ForumsUrl, MainViewModel.ReportBugUrl];

        Assert.All(links, link => Assert.True(Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == "https", link));
        Assert.StartsWith(MainViewModel.RepositoryUrl, MainViewModel.ReportBugUrl);
    }

    [Fact]
    public async Task Links_OpenThroughTheSystem()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;

        viewModel.OpenAboutLinkCommand.Execute(MainViewModel.DiscordUrl);
        viewModel.OpenAboutLinkCommand.Execute(MainViewModel.ForumsUrl);
        viewModel.OpenAboutLinkCommand.Execute(MainViewModel.ReportBugUrl);

        Assert.Equal([MainViewModel.DiscordUrl, MainViewModel.ForumsUrl, MainViewModel.ReportBugUrl], opened);
        Assert.Null(viewModel.AboutError);
    }

    [Fact]
    public async Task CopyDiagnostics_ReportsUnderTheButton()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.ShowHelpSettingsCommand.Execute(null);

        viewModel.ReportDiagnosticsCopied();

        Assert.Equal(harness.Localization.AboutCopied, viewModel.AboutMessage);
        Assert.Contains("Borea " + MainViewModel.BoreaInformationalVersion, viewModel.DiagnosticsWithLogText());
    }

    [Fact]
    public void HelpTexts_AreTranslatedIntoGerman()
    {
        var english = ReadHelpTexts("en");
        var german = ReadHelpTexts("de");

        Assert.Equal("Help", english[0]);
        Assert.Equal("Hilfe", german[0]);
        Assert.All(english.Zip(german), pair => Assert.NotEqual(pair.First, pair.Second));
    }

    private static string[] ReadHelpTexts(string culture)
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo(culture));
        return [localization.SettingsHelp, localization.HelpAsk, localization.HelpFacts, localization.HelpForums, localization.HelpReportHint];
    }
}
