using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class DiscoverViewModelTests
{
    [Fact]
    public async Task Load_ListsOnlyTheContentIndex()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal("index", item.Source));
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(ContentType.Mod, item.Type));
        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.ModId == ViewModelHarness.FakeSpaceDock.OwnId);
        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.ModId == ViewModelHarness.FakeSpaceDock.MirroredId);
        Assert.Equal(["MIT"], viewModel.LicenseOptions);
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

        viewModel.SearchText = "advanced flight";
        Assert.Equal(["AdvancedFlightComputer"], viewModel.DiscoverItems.Select(item => item.ModId));

        viewModel.SearchText = "laurens";
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == "KSArmory");

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
        Assert.False(viewModel.HasDiscoverItems);

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

        // without a game the compatibility is unknown, so the plan waits for a confirmation
        Assert.NotNull(item.InstallWarning);
        Assert.Null(item.InstallError);
        await item.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(item.InstallWarning);
        Assert.Null(item.PendingPlan);
        Assert.NotNull(item.InstallError);
        Assert.False(item.IsInstalling);
        Assert.Equal(0, item.Progress);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_WarningCancelled_InstallsNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");

        await item.InstallCommand.ExecuteAsync(null);
        Assert.Contains("AdvancedFlightComputer", item.InstallWarning);
        Assert.NotNull(item.PendingPlan);

        item.CancelInstallCommand.Execute(null);

        Assert.Null(item.InstallWarning);
        Assert.Null(item.PendingPlan);
        Assert.Null(item.InstallError);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesRows()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "KSArmory");
        var english = item.AuthorsText;

        harness.Localization.TrySetCulture("de");

        Assert.NotEqual(english, item.AuthorsText);
        Assert.Contains("Laurens", item.AuthorsText);
    }
}

/// <summary>
/// Puts a mod into an instance without downloading it: the release comes from
/// the index fixture. The files are marked as not Borea's unless the caller
/// says Borea installed them, and then the folder carries the ownership file
/// an install writes.
/// </summary>
internal static class InstalledContent
{
    public static async Task<Instance> AddAsync(ViewModelHarness harness, string modId, bool activate, InstallReason reason = InstallReason.Manual, ModInstallOwnership ownership = ModInstallOwnership.Foreign)
    {
        var services = harness.Services;
        var instance = (await services.Instances.GetAllAsync()).FirstOrDefault()
            ?? await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var release = await services.Mods.GetLatestReleaseAsync(modId)
            ?? throw new InvalidOperationException($"The fixture has no release of {modId}.");

        // the manifest only lists a mod whose folder holds a mod.toml
        var folder = Directory.CreateDirectory(Path.Combine(services.Paths.GetInstanceModsFolder(instance.InstanceId), modId));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{modId}\"");
        var token = ownership == ModInstallOwnership.Borea ? Guid.NewGuid().ToString("N") : null;
        if (token is not null)
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, ".borea-owner"), token);
        instance.AddMod(new InstalledMod(modId, release.Version, reason, DateTimeOffset.UtcNow, release, ownership: ownership, ownershipToken: token));
        await services.Instances.SaveAsync(instance);
        await services.ModState.AddEntryAsync(instance.InstanceId, modId, enabled: true);
        if (activate)
            await services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        return instance;
    }
}
