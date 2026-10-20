using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using static Borea.App.Tests.ViewModels.UserContentFixtures;

namespace Borea.App.Tests.ViewModels;

/// <summary>Vehicles and saves in Discover, on their page and in an instance (RFC 0085).</summary>
public sealed class UserContentViewModelTests
{
    [Fact]
    public async Task Discover_SnapshotWithoutVehicles_KeepsTheTabsDisabledAndTheModsUnchanged()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.False(viewModel.HasDiscoverVehicles);
        Assert.False(viewModel.HasDiscoverSaves);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(harness.Localization.FormatDiscoverModCount(3), viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task Discover_SnapshotWithAVehicle_EnablesItsTabAndListsIt()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.True(viewModel.HasDiscoverVehicles);
        Assert.False(viewModel.HasDiscoverSaves);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(harness.Localization.FormatDiscoverModCount(3), viewModel.DiscoverCountText);

        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        Assert.True(viewModel.IsVehiclesTab);
        Assert.False(viewModel.ShowsCategories);
        var row = Assert.Single(viewModel.DiscoverItems);
        Assert.Equal(("ExampleRover", "Example Rover", "A vehicle.", "Kitten"), (row.ModId, row.Name, row.Abstract, Assert.Single(row.AuthorLinks).Name));
        Assert.Equal(harness.Localization.ContentTypeVehicle, row.TypeText);
        Assert.Equal(42L, row.Downloads);
        Assert.Equal(">= 2026.8.19.5261", row.GameVersionText);
        Assert.Equal(ModVersion.Parse("1.1.0"), row.LatestInChannel!.Version);
        Assert.Equal(harness.Localization.FormatDiscoverVehicleCount(1), viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task Discover_SearchOnTheVehiclesTab_FindsOnlyVehicles_AndAModSearchFindsNoVehicle()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.SearchText = "Rover";
        Assert.Empty(viewModel.DiscoverItems);

        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        Assert.Equal("ExampleRover", Assert.Single(viewModel.DiscoverItems).ModId);

        viewModel.SearchText = "Flight";
        Assert.Empty(viewModel.DiscoverItems);
    }

    [Fact]
    public async Task Discover_ChosenCategory_DoesNotFilterTheVehiclesTab()
    {
        using var harness = await VehicleHarnessAsync(editSnapshot: ViewModelHarness.CuratedTags(("control", "Control")));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var control = viewModel.CategoryOptions.First(category => category.Tag == "control");
        viewModel.ToggleCategoryCommand.Execute(control);
        Assert.True(viewModel.HasDiscoverFilters);

        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        Assert.Equal("ExampleRover", Assert.Single(viewModel.DiscoverItems).ModId);
        Assert.False(viewModel.HasDiscoverFilters);
    }

    [Fact]
    public async Task Discover_VehicleRecordedInTheActiveInstance_IsInstalledAndHiddenByHideInstalled()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        var instance = await ActiveInstanceWithRoverAsync(harness);
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        Assert.True(Assert.Single(viewModel.DiscoverItems).IsInstalled);
        viewModel.HideInstalled = true;
        Assert.Empty(viewModel.DiscoverItems);
        Assert.Equal(ModVersion.Parse("1.0.0"), viewModel.ActiveInstance!.InstalledVersionOf("ExampleRover"));
        Assert.Equal(instance.InstanceId, viewModel.ActiveInstance.InstanceId);
    }

    [Fact]
    public async Task Discover_VehicleRecordedInAnotherInstance_ShowsUnderInstalledInOtherInstances()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await ActiveInstanceWithRoverAsync(harness);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(other.InstanceId);
        await viewModel.LoadAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);

        viewModel.InstalledInOtherInstances = true;

        var row = Assert.Single(viewModel.DiscoverItems);
        Assert.False(row.IsInstalled);
    }

    [Fact]
    public async Task Discover_FavoriteVehicle_ShowsUnderFavoritesOnly()
    {
        using var harness = await VehicleHarnessAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverVehiclesCommand.Execute(null);
        var row = Assert.Single(viewModel.DiscoverItems);

        await row.ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.FavoritesOnly = true;

        Assert.Same(row, Assert.Single(viewModel.DiscoverItems));
        Assert.True(row.IsFavorite);
    }

    /// <summary>The vehicle Example Rover with releases 1.0.0 and 1.1.0, served, with 42 downloads.</summary>
    internal static Task<ViewModelHarness> VehicleHarnessAsync(Func<string, string>? editSnapshot = null, string dependencies = "")
    {
        var rover = RoverArchive();
        var entry = EntryNeeding("ExampleRover", "vehicle", "Example Rover", RoverUrl, rover, dependencies, ["1.0.0", "1.1.0"])
            .Replace("\"id\": \"ExampleRover\",", "\"id\": \"ExampleRover\", \"downloads\": { \"total\": 42, \"hosts\": { \"github\": 42 } },", StringComparison.Ordinal);
        return ViewModelHarness.CreateAsync(
            respond: request => request.RequestUri?.AbsoluteUri == RoverUrl ? ArchiveResponse(rover) : null,
            editSnapshot: editSnapshot,
            typeFiles: TypeFiles(ContentIndexTypeFiles.Vehicles, entry));
    }

    /// <summary>An active instance that holds Example Rover 1.0.0 as Borea installs it.</summary>
    internal static async Task<Instance> ActiveInstanceWithRoverAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        var release = (await harness.Services.UserContent.GetReleaseAsync("ExampleRover", ModVersion.Parse("1.0.0")))!;
        Assert.True((await harness.Services.UserContentInstaller.InstallAsync(instance.InstanceId, release, name: null)).IsDone);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }
}
