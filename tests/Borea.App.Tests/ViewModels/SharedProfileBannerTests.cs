using Borea.App.ViewModels;
using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class SharedProfileBannerTests
{
    [Fact]
    public async Task SavedGameDirectory_ProfileWithMods_ShowsTheBannerWithTheCount()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        WriteProfileMod(harness, "Alpha");
        WriteProfileMod(harness, "Beta");

        await SaveGameDirectoryAsync(harness);

        Assert.True(harness.ViewModel.ShowSharedProfileBanner);
        Assert.True(harness.ViewModel.HasSharedProfileMods);
        Assert.Equal("2 mods in your game profile", harness.ViewModel.SharedProfileBannerText);
        harness.Localization.TrySetCulture("de");
        Assert.Equal("2 Mods in deinem Spielprofil", harness.ViewModel.SharedProfileBannerText);
    }

    [Fact]
    public async Task NoGameDirectory_HidesTheBannerAndKeepsTheLibraryAction()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        WriteProfileMod(harness, "Alpha");

        await harness.ViewModel.LoadAsync();

        Assert.False(harness.ViewModel.ShowSharedProfileBanner);
        Assert.True(harness.ViewModel.HasSharedProfileMods);
        Assert.Equal("1 mod in your game profile", harness.ViewModel.SharedProfileBannerText);
    }

    [Fact]
    public async Task EmptyProfile_HidesTheBannerAndTheLibraryAction()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        Directory.CreateDirectory(Path.Combine(ProfileFolder(harness), "mods", "NotAMod"));

        await SaveGameDirectoryAsync(harness);

        Assert.False(harness.ViewModel.ShowSharedProfileBanner);
        Assert.False(harness.ViewModel.HasSharedProfileMods);
    }

    [Fact]
    public async Task Dismiss_HidesTheBannerAndIsRemembered()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        WriteProfileMod(harness, "Alpha");
        await SaveGameDirectoryAsync(harness);

        viewModel.DismissSharedProfileBannerCommand.Execute(null);
        await viewModel.WhenPreferencesSavedAsync();
        await viewModel.LoadAsync();

        Assert.False(viewModel.ShowSharedProfileBanner);
        Assert.True(viewModel.HasSharedProfileMods);
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.True(saved.Preferences.SharedProfileBannerDismissed);
    }

    [Fact]
    public async Task CreateInstance_FromTheBanner_CopiesTheModsAndOpensTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        WriteProfileMod(harness, "LocalOnly");
        await SaveGameDirectoryAsync(harness);

        viewModel.BeginImportSharedProfileCommand.Execute(null);
        Assert.True(viewModel.IsCreatingInstance);
        Assert.True(viewModel.IsImportingSharedProfile);
        Assert.Equal(harness.Localization.SharedProfileInstanceName, viewModel.ModalInstanceName);
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);
        await viewModel.WhenPreferencesSavedAsync();

        Assert.Null(viewModel.InstanceError);
        Assert.False(viewModel.IsCreatingInstance);
        Assert.False(viewModel.IsImportingSharedProfile);
        Assert.False(viewModel.IsSharedProfileImportRunning);
        Assert.False(viewModel.ShowSharedProfileBanner);
        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Null(viewModel.SharedProfileImportNotice);
        Assert.Equal(harness.Localization.SharedProfileInstanceName, viewModel.SelectedInstance?.Name);
        var instance = Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.Equal("LocalOnly", Assert.Single(instance.ForeignMods).FolderName);
        Assert.True(File.Exists(Path.Combine(ProfileFolder(harness), "mods", "LocalOnly", "mod.toml")));
    }

    [Fact]
    public async Task CreateInstance_FromTheBanner_SaysWhatTheInstanceDoesNotTakeOver()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        WriteProfileManifest(harness, ("My Mod", true), ("MeasureTools", true));
        WriteProfileMod(harness, "My Mod");
        WriteProfileMod(harness, "MeasureTools");
        await SaveGameDirectoryAsync(harness);

        viewModel.BeginImportSharedProfileCommand.Execute(null);
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowInstance);
        Assert.Equal(
            "Borea could not enable these mods, because their folder names are not valid content ids: My Mod"
                + Environment.NewLine
                + "Borea could not compare these mods with the content index, so they stay manual installs: MeasureTools",
            viewModel.SharedProfileImportNotice);
    }

    [Fact]
    public async Task CloseWhileImporting_StopsTheImportAndKeepsTheModalTheUserOpened()
    {
        ViewModelHarness? owner = null;
        using var harness = await ViewModelHarness.CreateAsync(respond: _ =>
        {
            if (owner?.ViewModel.IsSharedProfileImportRunning == true)
            {
                owner.ViewModel.CancelNameModalCommand.Execute(null);
                owner.ViewModel.BeginCreateInstanceCommand.Execute(null);
                owner.ViewModel.ModalInstanceName = "Career";
            }

            return null;
        });
        owner = harness;
        var viewModel = harness.ViewModel;
        WriteProfileMod(harness, "MeasureTools");
        await SaveGameDirectoryAsync(harness);

        viewModel.BeginImportSharedProfileCommand.Execute(null);
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.Empty(await harness.Services.Instances.GetAllAsync());
        Assert.True(viewModel.IsCreatingInstance);
        Assert.False(viewModel.IsImportingSharedProfile);
        Assert.Equal("Career", viewModel.ModalInstanceName);
        Assert.False(viewModel.CurrentWindowInstance);
        Assert.True(viewModel.ShowSharedProfileBanner);
        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task CreateInstance_FromTheBanner_TakenName_KeepsTheModalOpenWithTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        WriteProfileMod(harness, "LocalOnly");
        await harness.Services.Instances.CreateAsync(harness.Localization.SharedProfileInstanceName, InstanceSource.Custom.Value);
        await SaveGameDirectoryAsync(harness);

        viewModel.BeginImportSharedProfileCommand.Execute(null);
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.InstanceError);
        Assert.True(viewModel.IsCreatingInstance);
        Assert.True(viewModel.ShowSharedProfileBanner);
        Assert.Single(viewModel.Instances);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateInstance_FromTheBanner_BlankName_KeepsTheModalOpenWithTheMessage(string name)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        WriteProfileMod(harness, "LocalOnly");
        await SaveGameDirectoryAsync(harness);

        viewModel.BeginImportSharedProfileCommand.Execute(null);
        viewModel.ModalInstanceName = name;
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ModalNameRequired, viewModel.InstanceError);
        Assert.True(viewModel.IsCreatingInstance);
        Assert.True(viewModel.IsImportingSharedProfile);
        Assert.True(viewModel.ShowSharedProfileBanner);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());

        viewModel.ModalInstanceName = "Career";

        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task NewInstance_AfterTheImportModalWasClosed_CreatesAnEmptyInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        WriteProfileMod(harness, "LocalOnly");
        await viewModel.LoadAsync();

        viewModel.BeginImportSharedProfileCommand.Execute(null);
        viewModel.CancelNameModalCommand.Execute(null);
        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.ModalInstanceName = "Career";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsImportingSharedProfile);
        var instance = Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.Equal("Career", instance.Name);
        Assert.False(Directory.Exists(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId)));
    }

    private static string ProfileFolder(ViewModelHarness harness) => harness.Services.Paths.GetSharedProfileRoot();

    private static void WriteProfileManifest(ViewModelHarness harness, params (string Id, bool Enabled)[] entries)
    {
        Directory.CreateDirectory(ProfileFolder(harness));
        File.WriteAllText(
            Path.Combine(ProfileFolder(harness), "manifest.toml"),
            string.Concat(entries.Select(entry => $"[[mods]]\nid = \"{entry.Id}\"\nenabled = {(entry.Enabled ? "true" : "false")}\n\n")));
    }

    private static void WriteProfileMod(ViewModelHarness harness, string folderName)
    {
        var folder = Path.Combine(ProfileFolder(harness), "mods", folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{folderName}\"");
    }

    private static async Task SaveGameDirectoryAsync(ViewModelHarness harness)
    {
        harness.ViewModel.GameDirectoryInput = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await harness.ViewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
    }
}
