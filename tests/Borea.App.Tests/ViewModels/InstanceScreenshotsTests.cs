using Borea.App.ViewModels;
using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class InstanceScreenshotsTests
{
    [Fact]
    public async Task OpenScreenshotsFolder_CreatesTheFolderAndOpensIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await CreateAsync(harness, "Main");
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var folder = harness.Services.Paths.GetInstanceScreenshotsFolder(instance.InstanceId);
        Assert.False(Directory.Exists(folder));

        harness.ViewModel.Instances.Single().OpenScreenshotsFolderCommand.Execute(null);

        Assert.True(Directory.Exists(folder));
        Assert.Equal([folder], opened);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_WithScreenshots_CopiesThemFirstUnlessTheBoxIsOff(bool keep)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await CreateAsync(harness, "Main/Rover");
        var screenshots = harness.Services.Paths.GetInstanceScreenshotsFolder(instance.InstanceId);
        WriteScreenshot(screenshots, "ksa_20261120_101500_1920x1080.png");
        WriteScreenshot(screenshots, "ksa_20261120_101600_1920x1080.png");
        var pictures = Path.Combine(harness.Root, "Pictures");
        viewModel.PicturesFolder = () => pictures;
        var kept = Path.Combine(pictures, "Kitten Space Agency", "Main_Rover");

        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);
        await viewModel.WhenDeleteModalScreenshotsLoadedAsync();
        Assert.Equal(harness.Localization.FormatModalDeleteInstanceScreenshots(2), viewModel.DeleteModalScreenshotsText);
        Assert.True(viewModel.KeepScreenshotsOnDelete);
        Assert.Equal(harness.Localization.FormatModalDeleteInstanceKeepScreenshotsTip(kept), viewModel.DeleteModalKeepScreenshotsTip);
        viewModel.KeepScreenshotsOnDelete = keep;
        await viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);

        Assert.Empty(await harness.Services.Instances.GetAllAsync());
        Assert.False(Directory.Exists(screenshots));
        if (!keep)
        {
            Assert.False(Directory.Exists(kept));
            Assert.Empty(viewModel.Toasts.Items);
            return;
        }

        Assert.Equal(["ksa_20261120_101500_1920x1080.png", "ksa_20261120_101600_1920x1080.png"], Directory.GetFiles(kept).Select(Path.GetFileName).Order());
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.True(toast.IsFinished);
        Assert.Equal(harness.Localization.FormatToastScreenshotsKept(2, "Main_Rover"), toast.Message);
        Assert.Equal(harness.Localization.LibraryOpenFolder, toast.ActionText);
    }

    [Fact]
    public async Task Delete_CopyFails_KeepsTheInstanceAndShowsAnErrorToast()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await CreateAsync(harness, "Main");
        var screenshots = harness.Services.Paths.GetInstanceScreenshotsFolder(instance.InstanceId);
        WriteScreenshot(screenshots, "ksa_20261120_101500_1920x1080.png");
        var pictures = Path.Combine(harness.Root, "Pictures");
        Directory.CreateDirectory(pictures);
        File.WriteAllText(Path.Combine(pictures, "Kitten Space Agency"), "a file where the folder should be");
        viewModel.PicturesFolder = () => pictures;

        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);
        await viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);

        Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.True(File.Exists(Path.Combine(screenshots, "ksa_20261120_101500_1920x1080.png")));
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.FormatToastScreenshotsKeepFailed("Main"), toast.Message);
    }

    [Fact]
    public async Task Delete_WithoutScreenshots_LooksAndWorksAsBefore()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await CreateAsync(harness, "Main");

        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);
        await viewModel.WhenDeleteModalScreenshotsLoadedAsync();
        Assert.Null(viewModel.DeleteModalScreenshotsText);
        await viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);

        Assert.Empty(await harness.Services.Instances.GetAllAsync());
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task DeleteModal_LanguageChange_RefreshesTheScreenshotsTexts()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await CreateAsync(harness, "Main");
        WriteScreenshot(harness.Services.Paths.GetInstanceScreenshotsFolder(instance.InstanceId), "ksa_20261120_101500_1920x1080.png");
        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);
        await viewModel.WhenDeleteModalScreenshotsLoadedAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Contains(nameof(MainViewModel.DeleteModalScreenshotsText), changed);
        Assert.Contains(nameof(MainViewModel.DeleteModalKeepScreenshotsTip), changed);
    }

    private static async Task<Instance> CreateAsync(ViewModelHarness harness, string name)
    {
        var instance = (await harness.Services.Instances.CreateAsync(name, InstanceSource.Custom.Value)).Instance;
        await harness.ViewModel.LoadAsync();
        return instance;
    }

    private static void WriteScreenshot(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), new byte[100]);
    }
}
