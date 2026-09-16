using Borea.App.ViewModels;
using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class GameLogViewModelTests
{
    [Fact]
    public async Task ShowLog_ShowsTheEndOfTheGameLog()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var path = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Loading mods\nGame started\n");

        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLogTab);
        Assert.False(viewModel.IsContentTab);
        Assert.False(viewModel.IsGameDataTab);
        Assert.True(viewModel.HasGameLog);
        Assert.Equal("Loading mods" + Environment.NewLine + "Game started", viewModel.GameLogText);
        Assert.Equal(MainViewModel.WithoutUserProfile(path), viewModel.GameLogPathText);
        Assert.Null(viewModel.GameLogError);
    }

    [Fact]
    public async Task ShowLog_RunLog_ShowsAndOpensTheRunLog()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var path = Path.Combine(Path.GetDirectoryName(harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId))!, "KittenSpaceAgency.260915-112433.43720.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "11:24:36.689  INFO loaded settings\n");

        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);

        Assert.Equal(MainViewModel.WithoutUserProfile(path), viewModel.GameLogPathText);
        Assert.True(viewModel.CanOpenGameLog);
    }

    [Fact]
    public async Task ShowLog_SplitsTheLinesByLevel()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var path = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "11:30:03.330  INFO loaded settings\n11:30:04.064  WARN slow start\n");

        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);

        Assert.Equal(new[] { GameLogLevel.Information, GameLogLevel.Warning }, viewModel.GameLogLines.Select(line => line.Severity));
    }

    [Fact]
    public async Task ReloadLog_ReadsTheLogAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);
        var path = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Game started\n");

        await viewModel.ReloadGameLogCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasGameLog);
        Assert.False(viewModel.IsGameLogMissing);
        Assert.Equal("Game started", Assert.Single(viewModel.GameLogLines).Message);
    }

    [Fact]
    public async Task ReportCopied_ShowsTheNoticeUntilTheNextRead()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var path = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Game started\n");
        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);

        viewModel.ReportGameLogCopied();
        Assert.Equal(viewModel.Localization.AboutCopied, viewModel.GameLogMessage);

        await viewModel.ReloadGameLogCommand.ExecuteAsync(null);
        Assert.Null(viewModel.GameLogMessage);
    }

    [Fact]
    public async Task ShowLog_NoLog_ShowsNothingAndCannotOpen()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenAsync(harness, "Main");
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;

        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);
        viewModel.OpenGameLogCommand.Execute(null);

        Assert.False(viewModel.HasGameLog);
        Assert.True(viewModel.IsGameLogMissing);
        Assert.False(viewModel.CanOpenGameLog);
        Assert.Null(viewModel.GameLogText);
        Assert.NotNull(viewModel.GameLogPathText);
        Assert.Empty(opened);
        Assert.NotNull(viewModel.GameLogError);
    }

    [Fact]
    public async Task ShowLog_ReadFails_ShowsTheErrorAndCanStillOpen()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        Directory.CreateDirectory(harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId));

        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.GameLogError);
        Assert.False(viewModel.HasGameLog);
        Assert.False(viewModel.IsGameLogMissing);
        Assert.True(viewModel.CanOpenGameLog);
    }

    [Fact]
    public async Task OpenLog_UsesTheSystemOpener()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        var path = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Game started\n");
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);

        viewModel.OpenGameLogCommand.Execute(null);

        Assert.Equal([path], opened);
        Assert.Null(viewModel.GameLogError);
    }

    [Fact]
    public async Task Reload_KeepsTheLogTabAndReadsAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await OpenAsync(harness, "Main");
        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasGameLog);
        var path = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Game started\n");

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsLogTab);
        Assert.Equal("Game started", viewModel.GameLogText);
    }

    private static async Task<Instance> OpenAsync(ViewModelHarness harness, string name)
    {
        var instance = await harness.Services.Instances.CreateAsync(name, InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        return instance;
    }
}
