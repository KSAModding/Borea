using System.ComponentModel;
using System.Globalization;
using Borea.App.ViewModels;
using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class GameDataViewModelTests
{
    [Fact]
    public async Task ShowGameData_ListsTheInstanceFolderWithSizes()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var paths = harness.Services.Paths;
        Write(Path.Combine(paths.GetInstanceSavesFolder(instance.InstanceId), "Orbit", "save.dat"), 1500);
        Write(paths.GetInstanceSettingsPath(instance.InstanceId), 12);

        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGameDataTab);
        Assert.False(viewModel.IsContentTab);
        Assert.Equal(["saves", "Vehicles", "settings.toml", "HUDLayouts", "crashdumps", "exports"], viewModel.GameDataItems.Select(item => item.Name));
        Assert.Equal(1.5.ToString("0.0", CultureInfo.CurrentCulture) + " KB", viewModel.GameDataItems[0].SizeText);
        Assert.Equal("12 B", viewModel.GameDataItems[2].SizeText);
        Assert.Null(viewModel.GameDataError);

        viewModel.ShowInstanceContentCommand.Execute(null);
        Assert.True(viewModel.IsContentTab);
        Assert.False(viewModel.IsGameDataTab);
    }

    [Fact]
    public async Task ShowGameData_MissingFolder_ShowsEmptyAndCannotOpen()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenAsync(harness, "Main");

        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        var vehicles = viewModel.GameDataItems.Single(item => item.Name == "Vehicles");
        Assert.False(vehicles.Exists);
        Assert.Equal(harness.Localization.GameDataEmpty, vehicles.SizeText);
    }

    [Fact]
    public async Task OpenFolder_UsesTheSystemOpener()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var paths = harness.Services.Paths;
        Directory.CreateDirectory(paths.GetInstanceSavesFolder(instance.InstanceId));
        Write(paths.GetInstanceSettingsPath(instance.InstanceId), 12);
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        viewModel.GameDataItems.Single(item => item.Name == "saves").OpenFolderCommand.Execute(null);
        viewModel.GameDataItems.Single(item => item.Name == "settings.toml").OpenFolderCommand.Execute(null);

        Assert.Equal([paths.GetInstanceSavesFolder(instance.InstanceId), paths.GetInstanceRoot(instance.InstanceId)], opened);
        Assert.Null(viewModel.GameDataError);
    }

    [Fact]
    public async Task OpenFolder_FolderGoneOrSystemFails_ReportsOnTheTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var saves = harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId);
        Directory.CreateDirectory(saves);
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
        var item = viewModel.GameDataItems.Single(item => item.Name == "saves");

        viewModel.OpenWithSystem = _ => throw new Win32Exception("No application is associated with the folder.");
        item.OpenFolderCommand.Execute(null);
        Assert.Equal("No application is associated with the folder.", viewModel.GameDataError);

        Directory.Delete(saves);
        item.OpenFolderCommand.Execute(null);
        Assert.Equal(harness.Localization.FormatAboutFolderMissing(saves), viewModel.GameDataError);
    }

    [Fact]
    public async Task OpenAnotherInstance_ReturnsToContent_AndReloadKeepsTheTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("First", InstanceSource.Custom.Value);
        await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var first = viewModel.Instances.Single(instance => instance.Name == "First");
        await first.OpenCommand.ExecuteAsync(null);
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        await viewModel.LoadAsync();
        Assert.True(viewModel.IsGameDataTab);
        Assert.Equal(6, viewModel.GameDataItems.Count);

        await viewModel.Instances.Single(instance => instance.Name == "Second").OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsContentTab);
    }

    [Fact]
    public async Task ShowGameData_AnotherInstance_DoesNotShowThePreviousRows()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var first = await harness.Services.Instances.CreateAsync("First", InstanceSource.Custom.Value);
        var second = await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(instance => instance.Name == "First").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
        await viewModel.Instances.Single(instance => instance.Name == "Second").OpenCommand.ExecuteAsync(null);
        var firstRoot = harness.Services.Paths.GetInstanceRoot(first.InstanceId);

        var loading = viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
        Assert.DoesNotContain(viewModel.GameDataItems, item => item.FolderPath.StartsWith(firstRoot, StringComparison.Ordinal));
        await loading;

        var secondRoot = harness.Services.Paths.GetInstanceRoot(second.InstanceId);
        Assert.Equal(6, viewModel.GameDataItems.Count);
        Assert.All(viewModel.GameDataItems, item => Assert.StartsWith(secondRoot, item.FolderPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LanguageChange_TranslatesEmptySizes()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);

        harness.Localization.TrySetCulture("de");

        Assert.All(viewModel.GameDataItems, item => Assert.Equal(harness.Localization.GameDataEmpty, item.SizeText));
        Assert.NotEqual("Empty", harness.Localization.GameDataEmpty);
    }

    [Fact]
    public async Task RegionalFormatChange_FormatsSizesAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        Write(Path.Combine(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "save.dat"), 1500);
        viewModel.RegionalFormat.TrySetCulture("en-US");
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
        Assert.Equal("1.5 KB", viewModel.GameDataItems[0].SizeText);

        viewModel.RegionalFormat.TrySetCulture("de-DE");

        Assert.Equal("1,5 KB", viewModel.GameDataItems[0].SizeText);
    }

    [Theory]
    [InlineData(999, "999 B")]
    [InlineData(1000, "1.0 KB")]
    [InlineData(999_949, "999.9 KB")]
    [InlineData(999_950, "1.0 MB")]
    [InlineData(2_500_000_000, "2.5 GB")]
    public async Task FormatSize_UsesDecimalUnits(long bytes, string expected)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        Assert.Equal(expected, harness.ViewModel.FormatGameDataSize(bytes));
    }

    private static async Task<Instance> OpenAsync(ViewModelHarness harness, string name)
    {
        var instance = await harness.Services.Instances.CreateAsync(name, InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        return instance;
    }

    private static void Write(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
