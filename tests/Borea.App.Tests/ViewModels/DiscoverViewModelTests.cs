using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class DiscoverViewModelTests
{
    [Fact]
    public async Task Load_ListsIndexModsAndHidesTheSpaceDockCopyOfAnIndexListing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.True(viewModel.HasDiscoverItems);
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == "AdvancedFlightComputer");
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == ViewModelHarness.FakeSpaceDock.OwnId);
        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.ModId == ViewModelHarness.FakeSpaceDock.MirroredId);
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(ContentType.Mod, item.Type));
        Assert.Contains("MIT", viewModel.LicenseOptions);
        Assert.Contains("GPL-3.0", viewModel.LicenseOptions);
        Assert.Null(viewModel.DiscoverError);
    }

    [Fact]
    public async Task LoadersTab_ShowsOnlyLoaders()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.ShowDiscoverLoadersCommand.Execute(null);

        Assert.True(viewModel.IsLoadersTab);
        var starMap = Assert.Single(viewModel.DiscoverItems);
        Assert.Equal("StarMap", starMap.ModId);
        Assert.False(starMap.CanInstall);
        Assert.Equal(harness.Localization.ContentTypeModLoader, starMap.TypeText);

        viewModel.ShowDiscoverModsCommand.Execute(null);
        Assert.True(viewModel.IsModsTab);
    }

    [Fact]
    public async Task Search_MatchesNameAbstractOrAuthor()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.SearchText = "aircraft";
        Assert.Equal([ViewModelHarness.FakeSpaceDock.OwnId], viewModel.DiscoverItems.Select(item => item.ModId));

        viewModel.SearchText = "someone";
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == ViewModelHarness.FakeSpaceDock.OwnId);

        viewModel.SearchText = "no such mod anywhere";
        Assert.False(viewModel.HasDiscoverItems);
    }

    [Fact]
    public async Task LicenseFilter_NarrowsTheListUntilCleared()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var all = viewModel.DiscoverItems.Count;

        viewModel.SelectLicenseCommand.Execute("GPL-3.0");
        Assert.True(viewModel.HasDiscoverFilters);
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal("GPL-3.0", item.License));

        viewModel.SelectOsCommand.Execute("windows");
        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.False(viewModel.HasDiscoverFilters);
        Assert.Equal(all, viewModel.DiscoverItems.Count);
    }

    [Fact]
    public async Task HideInstalled_DropsModsTheActiveInstanceHolds()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.True(afc.IsInstalled);
        Assert.False(afc.CanInstall);

        viewModel.HideInstalled = true;
        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.ModId == "AdvancedFlightComputer");
    }

    [Fact]
    public async Task Install_WithoutActiveInstance_DoesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.First();

        await item.InstallCommand.ExecuteAsync(null);

        Assert.Null(item.InstallError);
        Assert.False(item.IsInstalling);
    }

    [Fact]
    public async Task Install_DownloadFails_ShowsTheErrorOnTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");

        await item.InstallCommand.ExecuteAsync(null);

        Assert.NotNull(item.InstallError);
        Assert.False(item.IsInstalling);
        Assert.Equal(0, item.Progress);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_ListingWithoutRelease_ExplainsWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ViewModelHarness.FakeSpaceDock.OwnId);

        await item.InstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.DiscoverNoRelease, item.InstallError);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesRows()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ViewModelHarness.FakeSpaceDock.OwnId);
        var english = item.AuthorsText;

        harness.Localization.TrySetCulture("de");

        Assert.NotEqual(english, item.AuthorsText);
        Assert.Contains("Someone", item.AuthorsText);
    }
}

/// <summary>
/// Puts a mod into an instance without downloading it: the release comes from
/// the index fixture and the files are marked as not Borea's, so removing the
/// mod leaves the disk alone.
/// </summary>
internal static class InstalledContent
{
    public static async Task<Instance> AddAsync(ViewModelHarness harness, string modId, bool activate, InstallReason reason = InstallReason.Manual)
    {
        var services = harness.Services;
        var instance = (await services.Instances.GetAllAsync()).FirstOrDefault()
            ?? await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var release = await services.Mods.GetLatestReleaseAsync(modId)
            ?? throw new InvalidOperationException($"The fixture has no release of {modId}.");

        // the manifest only lists a mod whose folder holds a mod.toml
        var folder = Directory.CreateDirectory(Path.Combine(services.Paths.GetInstanceModsFolder(instance.InstanceId), modId));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{modId}\"");
        instance.AddMod(new InstalledMod(modId, release.Version, reason, DateTimeOffset.UtcNow, release, ownership: ModInstallOwnership.Foreign));
        await services.Instances.SaveAsync(instance);
        await services.ModState.AddEntryAsync(instance.InstanceId, modId, enabled: true);
        if (activate)
            await services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        return instance;
    }
}
