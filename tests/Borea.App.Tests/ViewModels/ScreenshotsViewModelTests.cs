using Borea.App.ViewModels;
using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class ScreenshotsViewModelTests
{
    /// <summary>Writes a screenshot with the name that the game gives it, and returns its file name.</summary>
    private static string WriteScreenshot(string folder, DateTime takenAt, byte[]? bytes = null)
    {
        var name = $"ksa_{takenAt:yyyyMMdd_HHmmss}_16x9.png";
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), bytes ?? ListingEditorTests.Png(16, 9));
        return name;
    }

    private static async Task<Instance> CreateInstanceAsync(ViewModelHarness harness, string name)
        => (await harness.Services.Instances.CreateAsync(name, InstanceSource.Custom.Value)).Instance;

    private static IEnumerable<string> FileNames(IEnumerable<ScreenshotItem> items) => items.Select(item => Path.GetFileName(item.Screenshot.Path));

    [Fact]
    public async Task ShowScreenshots_ListsEveryInstanceAndTheGameProfileNewestFirst_AndTheFilterShowsOneOfThem()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var alpha = await CreateInstanceAsync(harness, "Alpha");
        var beta = await CreateInstanceAsync(harness, "Beta");
        await CreateInstanceAsync(harness, "Gamma");
        var alphaOld = WriteScreenshot(paths.GetInstanceScreenshotsFolder(alpha.InstanceId), new DateTime(2026, 11, 1, 10, 0, 0));
        var alphaNew = WriteScreenshot(paths.GetInstanceScreenshotsFolder(alpha.InstanceId), new DateTime(2026, 11, 3, 10, 0, 0));
        var betaShot = WriteScreenshot(paths.GetInstanceScreenshotsFolder(beta.InstanceId), new DateTime(2026, 11, 2, 10, 0, 0));
        var profileShot = WriteScreenshot(paths.GetSharedProfileScreenshotsFolder(), new DateTime(2026, 11, 4, 10, 0, 0));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.ShowLibraryScreenshotsCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.True(viewModel.IsLibraryScreenshotsTab);
        Assert.Equal([profileShot, alphaNew, betaShot, alphaOld], FileNames(viewModel.ShownScreenshots));
        Assert.Equal([harness.Localization.SharedProfileInstanceName, "Alpha", "Beta", "Alpha"], viewModel.ShownScreenshots.Select(item => item.SourceName));
        Assert.Equal([harness.Localization.ScreenshotsAll, "Alpha", "Beta", harness.Localization.SharedProfileInstanceName], viewModel.ScreenshotFilters.Select(filter => filter.Name));
        Assert.Equal(harness.Localization.FormatScreenshotCount(4), viewModel.ScreenshotCountText);
        Assert.False(viewModel.ShowsScreenshotsEmpty);

        viewModel.SelectedScreenshotFilter = viewModel.ScreenshotFilters[1];
        await viewModel.ReloadScreenshotsCommand.ExecuteAsync(null);

        Assert.Equal("Alpha", viewModel.SelectedScreenshotFilter?.Name);
        Assert.Equal([alphaNew, alphaOld], FileNames(viewModel.ShownScreenshots));
        Assert.Equal(harness.Localization.FormatScreenshotCount(2), viewModel.ScreenshotCountText);
    }

    [Fact]
    public async Task ShowScreenshots_NoneAnywhere_ShowsTheEmptyTextAndNoFilter()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await CreateInstanceAsync(harness, "Main");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.ShowLibraryScreenshotsCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowsScreenshotsEmpty);
        Assert.False(viewModel.HasScreenshots);
        Assert.Empty(viewModel.ShownScreenshots);
    }
}
