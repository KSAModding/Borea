using Borea.App.Formatting;
using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class FavoritesViewModelTests
{
    private const string ModId = "AdvancedFlightComputer";

    private static readonly Func<string, string> TwoPacks = PackViewModelTests.WithPacks(
        PackViewModelTests.Pack("starter-pack", "Starter Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.10"))),
        PackViewModelTests.Pack("armory-pack", "Armory Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("KSArmory", "0.8.44"))));

    private static DiscoverItem Row(MainViewModel viewModel, string modId = ModId)
        => viewModel.DiscoverItems.Single(item => item.ModId == modId);

    [Fact]
    public async Task Favorite_SurvivesARestartAndShowsInEveryInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: TwoPacks);
        var main = await InstalledContent.AddAsync(harness, ModId, activate: true);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        await Row(viewModel).ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await viewModel.DiscoverPacks.Single(pack => pack.PackId == "armory-pack").ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.ShowDiscoverModsCommand.Execute(null);

        Assert.True(Row(viewModel).IsFavorite);
        Assert.Equal(harness.Localization.ContentRemoveFavorite, Row(viewModel).FavoriteText);
        Assert.Equal(harness.Localization.ContentAddFavorite, Row(viewModel, "KSArmory").FavoriteText);

        await harness.Services.Instances.SetActiveInstanceAsync(other.InstanceId);
        await viewModel.LoadAsync();
        Assert.Equal(other.InstanceId, viewModel.ActiveInstance!.InstanceId);
        Assert.True(Row(viewModel).IsFavorite);
        await viewModel.Instances.Single(instance => instance.InstanceId == main.InstanceId).OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.ContentGroups.SelectMany(group => group.Items).Single().Page!.IsFavorite);

        var restarted = new MainViewModel(harness.Localization, new RegionalFormatService(harness.Localization), appPreferencesRepository: null, AppPreferences.Empty, harness.Services);
        await restarted.EnsureDiscoverLoadedAsync();

        Assert.Equal([ModId], restarted.DiscoverItems.Where(item => item.IsFavorite).Select(item => item.ModId));
        restarted.ShowDiscoverModpacksCommand.Execute(null);
        Assert.Equal(["armory-pack"], restarted.DiscoverPacks.Where(pack => pack.IsFavorite).Select(pack => pack.PackId));
    }

    [Fact]
    public async Task StarOnAPageFromBeforeAnIndexReload_MarksEveryRowOfIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: TwoPacks);
        var main = await InstalledContent.AddAsync(harness, ModId, activate: true);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var page = Row(viewModel);
        await page.OpenCommand.ExecuteAsync(null);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var packPage = viewModel.DiscoverPacks.Single(pack => pack.PackId == "armory-pack");
        await packPage.OpenCommand.ExecuteAsync(null);
        viewModel.ShowDiscoverModsCommand.Execute(null);

        // each reload builds new rows, so the page, the instance row and Discover each hold a row of their own
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);
        await viewModel.Instances.Single(instance => instance.InstanceId == main.InstanceId).OpenCommand.ExecuteAsync(null);
        var instanceRow = viewModel.ContentGroups.SelectMany(group => group.Items).Single();
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);
        Assert.Equal(3, new[] { page, instanceRow.Page!, Row(viewModel) }.Distinct().Count());

        await page.ToggleFavoriteCommand.ExecuteAsync(null);
        await packPage.ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.FavoritesOnly = true;

        Assert.True(Row(viewModel).IsFavorite);
        Assert.Equal(harness.Localization.ContentRemoveFavorite, instanceRow.Page!.FavoriteText);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        Assert.NotSame(packPage, viewModel.DiscoverPacks.Single());
        Assert.Equal("armory-pack", viewModel.DiscoverPacks.Single().PackId);

        await viewModel.DiscoverPacks.Single().ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.ShowDiscoverModsCommand.Execute(null);
        await Row(viewModel).ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.False(packPage.IsFavorite);
        Assert.False(page.IsFavorite);
        Assert.Equal(harness.Localization.ContentAddFavorite, instanceRow.Page!.FavoriteText);
    }

    [Fact]
    public async Task OnlyFavorites_IsOfferedOnlyWhileAFavoriteExists_AndTurnsOffWithTheLast()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.False(viewModel.HasFavorites);

        await Row(viewModel).ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasFavorites);
        viewModel.FavoritesOnly = true;

        await Row(viewModel).ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasFavorites);
        Assert.False(viewModel.FavoritesOnly);
        Assert.False(viewModel.HasDiscoverFilters);
    }

    [Fact]
    public async Task FavoritesFilter_ShowsOnlyFavorites_AndClearAllClearsIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: TwoPacks);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var mods = viewModel.DiscoverItems.Count;
        await Row(viewModel).ToggleFavoriteCommand.ExecuteAsync(null);

        viewModel.FavoritesOnly = true;

        Assert.True(viewModel.HasDiscoverFilters);
        Assert.Equal([ModId], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(harness.Localization.FormatDiscoverCountShown(1, harness.Localization.FormatDiscoverModCount(mods)), viewModel.DiscoverCountText);

        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        Assert.Empty(viewModel.DiscoverPacks);
        viewModel.ClearFavoritesOnlyCommand.Execute(null);
        Assert.False(viewModel.HasDiscoverFilters);
        await viewModel.DiscoverPacks.Single(pack => pack.PackId == "armory-pack").ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.FavoritesOnly = true;
        Assert.Equal(["armory-pack"], viewModel.DiscoverPacks.Select(pack => pack.PackId));

        viewModel.ClearDiscoverFiltersCommand.Execute(null);

        Assert.False(viewModel.FavoritesOnly);
        Assert.False(viewModel.HasDiscoverFilters);
        Assert.Equal(2, viewModel.DiscoverPacks.Count);

        viewModel.ShowDiscoverModsCommand.Execute(null);
        Assert.Equal(mods, viewModel.DiscoverItems.Count);
    }

    [Fact]
    public async Task FavoritesFilter_DropsARowOnceItIsNoFavoriteAnyMore()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var row = Row(viewModel);
        await row.ToggleFavoriteCommand.ExecuteAsync(null);
        await Row(viewModel, "KSArmory").ToggleFavoriteCommand.ExecuteAsync(null);
        viewModel.FavoritesOnly = true;

        await row.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(["KSArmory"], await harness.Services.ModFavorites.GetFavoriteModIdsAsync());
    }

    [Fact]
    public async Task BrokenFavoritesFile_SaysSoAndTakesTheMarkBack()
    {
        const string Broken = "{ broken";
        using var harness = await ViewModelHarness.CreateAsync(seed: services => File.WriteAllTextAsync(services.Paths.GetModFavoritesPath(), Broken));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.Null(viewModel.DiscoverError);
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.ToastFavoritesReadFailed);
        var row = Row(viewModel);
        Assert.False(row.IsFavorite);

        await row.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.False(row.IsFavorite);
        Assert.Equal(harness.Localization.FormatToastFavoriteFailed(row.Name), viewModel.Toasts.Items[^1].Message);
        Assert.Equal(Broken, await File.ReadAllTextAsync(harness.Services.Paths.GetModFavoritesPath()));
    }
}
