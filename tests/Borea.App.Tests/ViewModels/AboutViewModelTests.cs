using Borea.App.ViewModels;
using Borea.Core.Instances;
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
        Assert.StartsWith(MainViewModel.BoreaVersion, MainViewModel.BoreaInformationalVersion);
        Assert.Contains(".NET", MainViewModel.RuntimeText);
        Assert.False(string.IsNullOrWhiteSpace(MainViewModel.SystemText));
        Assert.Contains(MainViewModel.Credits, credit => credit.Name == "Avalonia");
        Assert.Contains(MainViewModel.Credits, credit => credit.Name == "Tomlyn");
        Assert.All(MainViewModel.Credits, credit => Assert.StartsWith("https://", credit.Url));
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
        Assert.Equal(MainViewModel.WithoutUserProfile(viewModel.BoreaFolder!), viewModel.BoreaFolderText);
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

        Assert.StartsWith("Borea " + MainViewModel.BoreaInformationalVersion, text);
        Assert.Contains(MainViewModel.RuntimeText, text);
        Assert.Contains("KSA: not set up", text);
        Assert.Contains("StarMap: version unknown at " + MainViewModel.WithoutUserProfile(loader), text);
        Assert.DoesNotContain(Environment.UserName + Path.DirectorySeparatorChar, text);
        Assert.DoesNotContain("\n\n", text);

        viewModel.ReportDiagnosticsCopied();
        Assert.Equal(harness.Localization.AboutCopied, viewModel.AboutMessage);
        Assert.Null(viewModel.FolderMessage);
    }

    [Fact]
    public async Task Diagnostics_NameTodaysLogWithoutTheUserProfile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.EndsWith("Log: " + MainViewModel.WithoutUserProfile(harness.Services.Log.CurrentFilePath), viewModel.DiagnosticsText);
    }

    [Fact]
    public async Task DiagnosticsWithLog_AddTheEndOfTodaysLog()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        harness.Services.Log.Write("Install of Example 1.0.0 started.");

        var text = viewModel.DiagnosticsWithLogText();

        Assert.StartsWith(viewModel.DiagnosticsText + Environment.NewLine + Environment.NewLine + "End of today's log:", text);
        Assert.EndsWith("[app] Install of Example 1.0.0 started.", text);
    }

    [Fact]
    public async Task DiagnosticsWithLog_NoLog_IsTheDiagnostics()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        File.Delete(harness.Services.Log.CurrentFilePath);

        Assert.Equal(viewModel.DiagnosticsText, viewModel.DiagnosticsWithLogText());
    }

    [Fact]
    public async Task OpenBoreaLogAndLogFolder_OpenTodaysFileAndItsFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        harness.Services.Log.Write("Borea started.");

        viewModel.OpenBoreaLogCommand.Execute(null);
        viewModel.OpenLogFolderCommand.Execute(null);

        Assert.Equal([viewModel.BoreaLogFile!, viewModel.LogsFolder!], opened);
        Assert.Equal(Path.GetDirectoryName(viewModel.BoreaLogFile), viewModel.LogsFolder);
        Assert.StartsWith(Path.GetFullPath(harness.Root), Path.GetFullPath(viewModel.LogsFolder!));
        Assert.Null(viewModel.AboutError);
    }

    [Fact]
    public async Task OpenBoreaLog_NoLineToday_ReportsTheMissingFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        if (File.Exists(viewModel.BoreaLogFile))
            File.Delete(viewModel.BoreaLogFile);

        viewModel.OpenBoreaLogCommand.Execute(null);

        Assert.Equal(harness.Localization.FormatAboutFolderMissing(viewModel.BoreaLogFile!), viewModel.AboutError);
    }

    [Fact]
    public async Task CopyPath_ReportsUnderTheFolderButtons()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.ShowAboutSettingsCommand.Execute(null);

        viewModel.ReportFolderCopied();

        Assert.Equal(harness.Localization.AboutCopied, viewModel.FolderMessage);
        Assert.Null(viewModel.AboutMessage);

        viewModel.ShowAboutSettingsCommand.Execute(null);
        Assert.Null(viewModel.FolderMessage);
    }

    [Fact]
    public async Task Diagnostics_NameTheLoaderReleaseTheGameTabShows()
    {
        var loader = Path.Combine(Path.GetTempPath(), "StarMap");
        using var harness = await ViewModelHarness.CreateAsync(services => services.SettingsRepository.SaveAsync(
            services.Settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false))));

        Assert.Contains("StarMap: 0.4.6 at " + MainViewModel.WithoutUserProfile(loader), harness.ViewModel.DiagnosticsText);
    }

    [Fact]
    public async Task Diagnostics_NameTheGameFolderAndTheActiveInstance()
    {
        string game = null!;
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game)),
            candidates: harness => harness.Candidates.Games.Add(game = PlaceGame(harness)));
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();

        var text = viewModel.DiagnosticsText;

        Assert.Contains("Game folder: " + MainViewModel.WithoutUserProfile(game), text);
        Assert.Contains("Instance: Main", text);
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
        Assert.Equal(harness.Localization.FormatAboutCannotOpen("not a link"), viewModel.AboutError);

        viewModel.AboutError = null;
        viewModel.OpenAboutLinkCommand.Execute(null);
        Assert.Null(viewModel.AboutError);
    }

    [Fact]
    public void WithoutUserProfile_ReplacesOnlyTheProfilePrefix()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var inside = Path.Combine(profile, "AppData", "Local", "Borea", "Loaders", "StarMap");
        var elsewhere = Path.Combine(Path.GetPathRoot(profile) ?? "/", "Games", "StarMap");

        Assert.Equal("~" + Path.DirectorySeparatorChar + Path.Combine("AppData", "Local", "Borea", "Loaders", "StarMap"), MainViewModel.WithoutUserProfile(inside));
        Assert.Equal("~", MainViewModel.WithoutUserProfile(profile));
        Assert.Equal(elsewhere, MainViewModel.WithoutUserProfile(elsewhere));
        Assert.Equal(profile + "2", MainViewModel.WithoutUserProfile(profile + "2"));
    }

    private static string PlaceGame(ViewModelHarness harness)
    {
        var directory = Directory.CreateDirectory(Path.Combine(harness.Root, "KSA")).FullName;
        File.WriteAllText(Path.Combine(directory, "KSA.exe"), "game");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(directory, "KSA.dll"));
        return directory;
    }
}
