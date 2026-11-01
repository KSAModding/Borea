using System.Globalization;
using System.Security.Cryptography;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using Borea.Core.Preview;

namespace Borea.App.Tests.ViewModels;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task Load_ShowsTheIndexModsWithTheNewestReleaseFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.Equal(["MeasureTools", "AdvancedFlightComputer", "KSArmory"], viewModel.RecentItems.Select(item => item.ModId));
        Assert.DoesNotContain(viewModel.RecentItems, item => item.ModId == "StarMap");
        var released = (await harness.Services.ContentIndex.GetLatestReleaseAsync("MeasureTools"))!.ReleaseDate;
        Assert.Equal(harness.Localization.FormatTimeAgoShort(DateTimeOffset.UtcNow - released), viewModel.RecentItems[0].FactText);
        Assert.Equal($"Updated on {MainViewModel.DateText(released)}", viewModel.RecentItems[0].FactToolTip);
    }

    private static readonly DateTimeOffset RoverListedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// KSArmory came into the index last and MeasureTools before it, and AdvancedFlightComputer does not say when.
    /// AdvancedFlightComputer has the most downloads, KSArmory fewer, and MeasureTools none.
    /// StarMap has the newest date and the highest count, but it is a loader.
    /// </summary>
    private static string ModDates(string json) =>
        With(With(With(With(json,
            "KSArmory", """ "listed_at": "2026-10-02T00:00:00Z", "downloads": { "total": 20, "hosts": { "github": 20 } },"""),
            "MeasureTools", """ "listed_at": "2026-09-02T00:00:00Z", "downloads": { "total": 0, "hosts": { "github": 0 } },"""),
            "AdvancedFlightComputer", """ "downloads": { "total": 1234, "hosts": { "github": 1234 } },"""),
            "StarMap", """ "listed_at": "2026-10-03T00:00:00Z", "downloads": { "total": 9000, "hosts": { "github": 9000 } },""");

    /// <summary>
    /// A Buggy came in after the rover, a third vehicle does not say when, and a fourth has only a dev release.
    /// A save that came in last is not a vehicle.
    /// </summary>
    internal static Dictionary<string, string> Vehicles()
    {
        var archive = UserContentFixtures.RoverArchive();
        string Vehicle(string id, string name) => UserContentFixtures.Entry(id, "vehicle", name, UserContentFixtures.RoverUrl, archive, "1.0.0");
        var vehicles = string.Join(", ",
            With(Vehicle("ExampleRover", "Example Rover"), "ExampleRover", """ "listed_at": "2026-10-01T00:00:00Z","""),
            With(Vehicle("Buggy", "Buggy"), "Buggy", """ "listed_at": "2026-10-05T00:00:00Z","""),
            Vehicle("Unlisted", "Unlisted"),
            With(Vehicle("DevOnly", "Dev Only"), "DevOnly", """ "listed_at": "2026-10-06T00:00:00Z",""").Replace("\"release_status\": \"stable\"", "\"release_status\": \"dev\"", StringComparison.Ordinal));
        var save = With(UserContentFixtures.SaveEntry(("1.0.0", archive)), "CampaignSave", """ "listed_at": "2026-10-07T00:00:00Z",""");
        return UserContentFixtures.TypeFiles(ContentIndexTypeFiles.Vehicles, vehicles)
            .Concat(UserContentFixtures.TypeFiles(ContentIndexTypeFiles.Saves, save))
            .ToDictionary();
    }

    internal static string With(string json, string id, string members)
    {
        var marker = $"\"id\": \"{id}\",";
        return json.Insert(json.IndexOf(marker, StringComparison.Ordinal) + marker.Length, members);
    }

    private static Task<ViewModelHarness> CreateWithGroupsAsync() => ViewModelHarness.CreateAsync(editSnapshot: ModDates, typeFiles: Vehicles());

    private static string[] IdsOf(HomeGroup group) => group.Items.Select(item => item.ModId).ToArray();

    [Fact]
    public async Task Load_FillsEveryGroupInTheOrderOfItsDiscoverSort()
    {
        using var harness = await CreateWithGroupsAsync();
        var viewModel = harness.ViewModel;

        Assert.Equal([harness.Localization.HomeRecentlyUpdated, harness.Localization.HomeNewMods, harness.Localization.HomeNewVehicles, harness.Localization.HomeMostDownloaded], viewModel.HomeGroups.Select(group => group.Title));
        Assert.Equal(["KSArmory", "MeasureTools"], IdsOf(viewModel.HomeGroups[1]));
        Assert.Equal(["Buggy", "ExampleRover"], IdsOf(viewModel.HomeGroups[2]));
        Assert.Equal(["AdvancedFlightComputer", "KSArmory"], IdsOf(viewModel.HomeGroups[3]));

        var rover = viewModel.HomeGroups[2].Items[1];
        Assert.Equal(harness.Localization.FormatTimeAgoShort(DateTimeOffset.UtcNow - RoverListedAt), rover.FactText);
        Assert.Equal(harness.Localization.FormatHomeListedOn(MainViewModel.DateText(RoverListedAt)), rover.FactToolTip);
        Assert.False(rover.ShowsDownloads);

        var mostDownloaded = viewModel.HomeGroups[3].Items[0];
        Assert.Equal(MainViewModel.CompactCount(1234), mostDownloaded.FactText);
        Assert.Equal(harness.Localization.FormatContentDownloadsExact(1234.ToString("N0", CultureInfo.CurrentCulture)), mostDownloaded.FactToolTip);
        Assert.True(mostDownloaded.ShowsDownloads);
    }

    [Fact]
    public async Task Load_GivesAVehicleWithoutAnIconTheStillThatDiscoverStored_AndAModNone()
    {
        var sha256 = Convert.ToHexString(SHA256.HashData(UserContentFixtures.RoverArchive()));
        using var harness = await ViewModelHarness.CreateAsync(WithGameAsync, editSnapshot: ModDates, typeFiles: Vehicles());
        var viewModel = harness.ViewModel;
        Assert.All(viewModel.HomeGroups[2].Items, tile => Assert.Null(tile.StillPath));

        var build = PreviewGameBuild.Of(harness.Services.InstalledVersion.GetInstalledVersion());
        Assert.NotNull(build);
        harness.Services.PreviewThumbnails.Store(sha256, build, "scene", [1, 2, 3]);
        await viewModel.LoadAsync();

        var stored = harness.Services.PreviewThumbnails.TryGetPath(sha256, build);
        Assert.NotNull(stored);
        Assert.All(viewModel.HomeGroups[2].Items, tile => Assert.Equal((stored, true), (tile.StillPath, tile.HasStill)));
        Assert.All(viewModel.HomeGroups[1].Items.Concat(viewModel.HomeGroups[3].Items), tile => Assert.False(tile.HasStill));
    }

    private static async Task WithGameAsync(BoreaServices services)
    {
        var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
        await services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
    }

    [Fact]
    public async Task Load_ShowsAGroupWithAtMostTheTilesOfTwoFullRows()
    {
        var archive = UserContentFixtures.RoverArchive();
        var vehicles = Enumerable.Range(0, MainViewModel.RecentItemCount + 1)
            .Select(number => $"Rover{number:00}")
            .Select(id => With(UserContentFixtures.Entry(id, "vehicle", id, UserContentFixtures.RoverUrl, archive, "1.0.0"), id, """ "listed_at": "2026-10-05T00:00:00Z","""));
        using var harness = await ViewModelHarness.CreateAsync(typeFiles: UserContentFixtures.TypeFiles(ContentIndexTypeFiles.Vehicles, string.Join(", ", vehicles)));

        Assert.Equal(MainViewModel.RecentItemCount, harness.ViewModel.HomeGroups[2].Items.Count);
        Assert.Equal("Rover00", harness.ViewModel.HomeGroups[2].Items[0].ModId);
    }

    [Theory]
    [InlineData(0, ContentType.Mod, DiscoverSortOrder.RecentlyUpdated)]
    [InlineData(1, ContentType.Mod, DiscoverSortOrder.RecentlyListed)]
    [InlineData(2, ContentType.Vehicle, DiscoverSortOrder.RecentlyListed)]
    [InlineData(3, ContentType.Mod, DiscoverSortOrder.Popularity)]
    public async Task GroupLink_OpensTheSameOrderOnDiscoverAndKeepsTheSavedSort(int group, ContentType type, DiscoverSortOrder order)
    {
        using var harness = await ViewModelHarness.CreateAsync(
            seed: services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithDiscoverSortOrder(DiscoverSortOrder.Name), MainViewModel.BundledThemeNames),
            editSnapshot: ModDates,
            typeFiles: Vehicles());
        var viewModel = harness.ViewModel;
        var tiles = IdsOf(viewModel.HomeGroups[group]);

        viewModel.HomeGroups[group].OpenDiscoverCommand.Execute(null);
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.WhenPreferencesSavedAsync();

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.Equal(type, viewModel.DiscoverType);
        Assert.Equal(order, viewModel.DiscoverSort);
        Assert.Equal(tiles, viewModel.DiscoverItems.Where(item => tiles.Contains(item.ModId)).Select(item => item.ModId));
        Assert.Equal(DiscoverSortOrder.Name, (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.DiscoverSortOrder);
    }

    [Fact]
    public async Task MenuPick_SavesTheSortThatAGroupLinkShows()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            seed: services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithDiscoverSortOrder(DiscoverSortOrder.Name), MainViewModel.BundledThemeNames));
        var viewModel = harness.ViewModel;

        viewModel.HomeGroups[0].OpenDiscoverCommand.Execute(null);
        viewModel.SelectDiscoverSortCommand.Execute(DiscoverSortOrder.RecentlyUpdated);
        await viewModel.WhenPreferencesSavedAsync();

        Assert.Equal(DiscoverSortOrder.RecentlyUpdated, (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences.DiscoverSortOrder);
    }

    [Fact]
    public async Task Load_BreaksATieOfTheReleaseDateByNameLikeTheDiscoverSort()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => json
            .Replace("2026-09-02T09:48:03Z", "2026-09-02T09:48:05Z", StringComparison.Ordinal)
            .Replace("Advanced Flight Computer", "Zulu Computer", StringComparison.Ordinal));
        var viewModel = harness.ViewModel;

        viewModel.HomeGroups[0].OpenDiscoverCommand.Execute(null);
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.Equal(["MeasureTools", "AdvancedFlightComputer", "KSArmory"], viewModel.RecentItems.Select(item => item.ModId));
        var tiles = IdsOf(viewModel.HomeGroups[0]);
        Assert.Equal(tiles, viewModel.DiscoverItems.Where(item => tiles.Contains(item.ModId)).Select(item => item.ModId));
    }

    [Fact]
    public async Task OpenVehicleTile_ShowsTheVehiclePageFromHome()
    {
        using var harness = await CreateWithGroupsAsync();
        var viewModel = harness.ViewModel;

        await viewModel.HomeGroups[2].Items[0].OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsContentFromHome);
        Assert.Equal("Buggy", viewModel.SelectedContent?.ModId);
        Assert.Equal(ContentType.Vehicle, viewModel.SelectedContent?.Type);
    }

    [Fact]
    public async Task LanguageChange_TranslatesTheGroupsAndTheirTiles()
    {
        using var harness = await CreateWithGroupsAsync();
        var group = harness.ViewModel.HomeGroups[2];
        var tile = group.Items[0];
        var changed = new List<string?>();
        group.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        tile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Equal([nameof(HomeGroup.Title), nameof(HomeGroup.LinkText), nameof(RecentItem.TypeText), nameof(RecentItem.FactText), nameof(RecentItem.FactToolTip)], changed.Distinct());
        Assert.Equal(harness.Localization.HomeNewVehicles, group.Title);
        Assert.Equal(harness.Localization.ContentTypeVehicle, tile.TypeText);
    }

    [Fact]
    public async Task OpenCard_ShowsTheContentPageWithTheDiscoverRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("AdvancedFlightComputer", viewModel.SelectedContent?.ModId);
        Assert.Contains(viewModel.DiscoverItems, item => ReferenceEquals(item, viewModel.SelectedContent));
    }

    [Fact]
    public async Task SwitchOffOnHome_KeepsTheInstanceUntilTheNextVisit()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        Assert.Same(viewModel.ActiveInstance, viewModel.HomeInstance);

        await viewModel.HomeInstance!.ToggleActiveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ActiveInstance);
        Assert.Equal("Alpha", viewModel.HomeInstance?.Name);
        Assert.False(viewModel.HomeInstance!.IsActive);

        await viewModel.HomeInstance.ToggleActiveCommand.ExecuteAsync(null);
        Assert.Same(viewModel.ActiveInstance, viewModel.HomeInstance);

        await viewModel.HomeInstance!.ToggleActiveCommand.ExecuteAsync(null);
        viewModel.SetMainWindowLibrary();
        viewModel.SetMainWindowHome();

        Assert.Null(viewModel.HomeInstance);
        Assert.False(viewModel.HasHomeInstance);
    }

    [Fact]
    public async Task InstanceActivatedElsewhere_ShowsOnHome()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        viewModel.SetMainWindowLibrary();

        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        viewModel.SetMainWindowHome();

        Assert.Same(viewModel.ActiveInstance, viewModel.HomeInstance);
        Assert.Equal("Alpha", viewModel.HomeInstance?.Name);
    }
}
