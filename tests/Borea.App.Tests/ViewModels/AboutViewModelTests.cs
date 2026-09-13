using Borea.App.ViewModels;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class AboutViewModelTests
{
    [Fact]
    public async Task ShowAbout_SelectsTheTabAndClearsOldMessages()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.AboutMessage = "old";
        viewModel.AboutError = "old";

        viewModel.ShowAboutSettingsCommand.Execute(null);

        Assert.True(viewModel.IsAboutTab);
        Assert.False(viewModel.IsGeneralTab);
        Assert.False(viewModel.IsGameTab);
        Assert.Null(viewModel.AboutMessage);
        Assert.Null(viewModel.AboutError);

        viewModel.ShowGeneralSettingsCommand.Execute(null);
        Assert.True(viewModel.IsGeneralTab);
        Assert.False(viewModel.IsAboutTab);
    }

    [Fact]
    public void VersionAndEnvironment_AreFilled()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", MainViewModel.BoreaVersion);
        Assert.DoesNotContain("+", MainViewModel.BoreaVersion);
        Assert.Contains(".NET", MainViewModel.RuntimeText);
        Assert.False(string.IsNullOrWhiteSpace(MainViewModel.SystemText));
        Assert.Contains(MainViewModel.ThirdPartyNotices, notice => notice.Name == "IBM Plex" && notice.License.Contains("OFL"));
        Assert.Contains(MainViewModel.ThirdPartyNotices, notice => notice.Name == "Phosphor Icons" && notice.License == "MIT");
    }

    [Fact]
    public void Links_AreAbsoluteUrlsUnderTheProjectHosts()
    {
        string[] links = [MainViewModel.RepositoryUrl, MainViewModel.ReportBugUrl, MainViewModel.ReleasesUrl, MainViewModel.CommunityUrl, MainViewModel.DiscordUrl];

        Assert.All(links, link => Assert.True(Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == "https", link));
        Assert.StartsWith("https://github.com/KSAModding", MainViewModel.CommunityUrl);
        Assert.StartsWith("https://discord.gg/", MainViewModel.DiscordUrl);
    }

    [Fact]
    public async Task Folders_PointIntoTheBoreaRoot()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.Equal(Path.GetFullPath(harness.Root), Path.GetFullPath(viewModel.BoreaFolder!));
        Assert.StartsWith(Path.GetFullPath(harness.Root), Path.GetFullPath(viewModel.InstancesFolder!));
    }

    [Fact]
    public async Task Diagnostics_ListVersionsGameAndLoaders()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var loader = Directory.CreateDirectory(Path.Combine(harness.Root, "StarMap")).FullName;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        viewModel.LoaderDirectoryInput = loader;
        await viewModel.AdoptLoaderCommand.ExecuteAsync(null);

        var text = viewModel.DiagnosticsText;

        Assert.StartsWith("Borea " + MainViewModel.BoreaVersion, text);
        Assert.Contains(MainViewModel.RuntimeText, text);
        Assert.Contains("KSA: not set up", text);
        Assert.Contains("StarMap: version unknown at " + loader, text);
        Assert.DoesNotContain("\n\n", text);

        viewModel.ReportDiagnosticsCopied();
        Assert.Equal(harness.Localization.AboutCopied, viewModel.AboutMessage);
    }

    [Fact]
    public async Task Diagnostics_NameTheLoaderReleaseTheGameTabShows()
    {
        var loader = Path.Combine(Path.GetTempPath(), "StarMap");
        using var harness = await ViewModelHarness.CreateAsync(services => services.SettingsRepository.SaveAsync(
            services.Settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false))));

        Assert.Contains("StarMap: 0.4.6 at " + loader, harness.ViewModel.DiagnosticsText);
    }

    [Fact]
    public async Task OpenFolder_MissingFolder_ReportsInsteadOfStarting()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.OpenInstancesFolderCommand.Execute(null);

        Assert.Equal(harness.Localization.FormatAboutFolderMissing(viewModel.InstancesFolder!), viewModel.AboutError);
    }

    [Fact]
    public async Task OpenLink_NotAUrlOrFolder_ReportsInsteadOfStarting()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.OpenAboutLinkCommand.Execute("not a link");
        Assert.NotNull(viewModel.AboutError);

        viewModel.OpenAboutLinkCommand.Execute(null);
        Assert.NotNull(viewModel.AboutError);
    }
}
