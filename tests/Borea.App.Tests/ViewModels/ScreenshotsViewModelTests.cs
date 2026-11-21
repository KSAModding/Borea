using Borea.App.ViewModels;
using Borea.Core.Instances;
using Borea.Core.Sharing;

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

    [Fact]
    public async Task GameData_InstanceWithScreenshots_ShowsTheNewestAndShowAllFiltersTheLibraryToIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var paths = harness.Services.Paths;
        var main = await CreateInstanceAsync(harness, "Main");
        var other = await CreateInstanceAsync(harness, "Other");
        var names = Enumerable.Range(1, 7).Select(day => WriteScreenshot(paths.GetInstanceScreenshotsFolder(main.InstanceId), new DateTime(2026, 11, day, 10, 0, 0))).ToList();
        WriteScreenshot(paths.GetInstanceScreenshotsFolder(other.InstanceId), new DateTime(2026, 11, 20, 10, 0, 0));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(item => item.Name == "Main").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        var strip = Assert.IsType<ScreenshotStrip>(viewModel.InstanceScreenshots);
        Assert.Equal(Enumerable.Reverse(names).Take(MainViewModel.RecentScreenshotCount), FileNames(strip.Items));

        await strip.ShowAllCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.True(viewModel.IsLibraryScreenshotsTab);
        Assert.Equal("Main", viewModel.SelectedScreenshotFilter?.Name);
        Assert.Equal(Enumerable.Reverse(names), FileNames(viewModel.ShownScreenshots));
    }

    [Fact]
    public async Task GameData_InstanceWithoutScreenshots_HasNoStrip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var other = await CreateInstanceAsync(harness, "Other");
        await CreateInstanceAsync(harness, "Main");
        WriteScreenshot(harness.Services.Paths.GetInstanceScreenshotsFolder(other.InstanceId), new DateTime(2026, 11, 20, 10, 0, 0));
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(item => item.Name == "Main").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        Assert.Null(viewModel.InstanceScreenshots);
    }

    [Fact]
    public async Task Share_OffersTheScreenshotsOfTheInstance_AndAddsOneAboveTheLimitAsASmallerJpeg()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instanceId = await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        var folder = harness.Services.Paths.GetInstanceScreenshotsFolder(instanceId);
        var small = WriteScreenshot(folder, new DateTime(2026, 11, 1, 10, 0, 0));
        var large = WriteScreenshot(folder, new DateTime(2026, 11, 2, 10, 0, 0), SharePictureFitTests.Noise(2000, 1600, opaque: true));
        Assert.InRange(new FileInfo(Path.Combine(folder, large)).Length, ShareImageLimits.MaxBytes + 1, SharePictureFit.MaxSourceBytes);
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        await editor.ShareScreenshotsLoad;

        var choices = Assert.IsAssignableFrom<IReadOnlyList<ShareScreenshotChoice>>(editor.ShownShareScreenshots);

        Assert.Equal([large, small], choices.Select(choice => Path.GetFileName(choice.Screenshot.Path)));

        await choices[1].AddCommand.ExecuteAsync(null);
        await choices[0].AddCommand.ExecuteAsync(null);

        Assert.Null(editor.SharePictureError);
        Assert.Equal([small, Path.ChangeExtension(large, ".jpg")], editor.SharePictures.Select(row => row.Name));
        Assert.InRange(editor.SharePictures[1].Bytes.Length, 1, ShareImageLimits.MaxBytes);
    }

    [Fact]
    public async Task GameEnds_InstanceGotScreenshotsDuringTheSession_OneToastCountsThemAndShowsThem()
    {
        var (harness, starter, instance) = await GameCrashToastTests.PlayAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;

        // the toast closes after its time, which a busy test run can pass before the check after the game ends is done
        viewModel.Toasts.Clock = new ToastViewModelTests.ManualTimeProvider();
        var folder = harness.Services.Paths.GetInstanceScreenshotsFolder(instance.InstanceId);
        WriteScreenshot(folder, DateTime.Now.AddDays(-1));
        WriteScreenshot(folder, DateTime.Now.AddMinutes(1));
        WriteScreenshot(folder, DateTime.Now.AddMinutes(2));

        starter.Exit(0);
        await viewModel.WhenGameExitCheckedAsync().WaitAsync(TimeSpan.FromSeconds(60));

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatNewScreenshots(2), toast.Message);
        Assert.Equal(harness.Localization.ScreenshotsShow, toast.ActionText);

        toast.RunActionCommand.Execute(null);
        await viewModel.WhenScreenshotsLoadedAsync();

        Assert.True(viewModel.IsLibraryScreenshotsTab);
        Assert.Equal("Main", viewModel.SelectedScreenshotFilter?.Name);
        Assert.Equal(3, viewModel.ShownScreenshots.Count);
    }
}
