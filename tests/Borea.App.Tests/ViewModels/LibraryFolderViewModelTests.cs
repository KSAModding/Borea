using Borea.App.ViewModels;
using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class LibraryFolderViewModelTests : IDisposable
{
    private readonly string _library = Path.Combine(Path.GetTempPath(), "BoreaAppLibrary_" + Guid.NewGuid());

    [Fact]
    public async Task LibraryFolder_NothingSaved_IsTheDefaultFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        Assert.Equal(harness.Root, harness.ViewModel.LibraryFolder);
        Assert.True(harness.ViewModel.IsDefaultLibraryFolder);
    }

    [Fact]
    public async Task ChangeLibraryFolder_MovesTheInstancesAndRebuildsTheServices()
    {
        using var harness = await ViewModelHarness.CreateAsync(seed: services => services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value));
        var viewModel = harness.ViewModel;

        await viewModel.ChangeLibraryFolderCommand.ExecuteAsync(_library);

        Assert.Null(viewModel.LibraryFolderError);
        Assert.Equal(harness.Localization.FormatLibraryFolderMoved(_library), viewModel.LibraryFolderMessage);
        Assert.False(viewModel.IsChangingLibraryFolder);
        Assert.Null(viewModel.LibraryFolderProgressText);
        Assert.Equal(_library, viewModel.LibraryFolder);
        Assert.False(viewModel.IsDefaultLibraryFolder);
        Assert.Equal(Path.Combine(_library, "Instances"), viewModel.InstancesFolder);
        Assert.Equal(_library, harness.Services.Settings.LibraryFolderPath);
        Assert.Equal("Alpha", Assert.Single(viewModel.Instances).Name);

        await viewModel.UseDefaultLibraryFolderCommand.ExecuteAsync(null);

        Assert.Null(viewModel.LibraryFolderError);
        Assert.Equal(harness.Root, viewModel.LibraryFolder);
        Assert.True(viewModel.IsDefaultLibraryFolder);
        Assert.Equal("Alpha", Assert.Single(viewModel.Instances).Name);
    }

    [Fact]
    public async Task ChangeLibraryFolder_FolderInsideTheLibrary_ShowsWhyAndKeepsTheFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.ChangeLibraryFolderCommand.ExecuteAsync(Path.Combine(harness.Root, "Library"));

        Assert.Equal(harness.Localization.FormatLibraryFolderInsideCurrent(harness.Root), viewModel.LibraryFolderError);
        Assert.Null(viewModel.LibraryFolderMessage);
        Assert.Equal(harness.Root, viewModel.LibraryFolder);
    }

    [Fact]
    public async Task LaunchWhileTheLibraryFolderChanges_IsRefused()
    {
        using var harness = await ViewModelHarness.CreateAsync(seed: async services =>
        {
            var instance = await services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
            await services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        });
        var viewModel = harness.ViewModel;
        viewModel.IsChangingLibraryFolder = true;

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LibraryFolderBusy, viewModel.LaunchMessage);
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task InstallWhileTheLibraryFolderChanges_IsRefused()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var item = await AfcRowAsync(harness);
        harness.ViewModel.IsChangingLibraryFolder = true;

        await item.InstallCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LibraryFolderBusy, item.InstallError);
        Assert.False(item.IsInstalling);
        Assert.Null(item.PendingPlan);
    }

    [Fact]
    public async Task ChangeLibraryFolder_WhileAnInstallRuns_IsRefused()
    {
        using var download = new ManualResetEventSlim();
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
        {
            if (IsAfcDownload(request.RequestUri!))
                download.Wait(TimeSpan.FromSeconds(30));
            return null;
        });
        var viewModel = harness.ViewModel;
        var item = await AfcRowAsync(harness);
        await item.InstallCommand.ExecuteAsync(null);

        var install = item.ConfirmInstallCommand.ExecuteAsync(null);
        for (var wait = 0; wait < 300 && !harness.Requests.Any(IsAfcDownload); wait++)
            await Task.Delay(100);
        await viewModel.ChangeLibraryFolderCommand.ExecuteAsync(_library);
        download.Set();
        await install;

        Assert.Equal(harness.Localization.LibraryFolderWaitForTask, viewModel.LibraryFolderError);
        Assert.Equal(harness.Root, viewModel.LibraryFolder);
        Assert.False(Directory.Exists(_library));
    }

    [Fact]
    public async Task StopLibraryFolderChange_ReturnsWhenTheChangeEnded_WithTheLibraryInOnePlace()
    {
        using var harness = await ViewModelHarness.CreateAsync(seed: services => services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value));
        var viewModel = harness.ViewModel;

        var change = viewModel.ChangeLibraryFolderCommand.ExecuteAsync(_library);
        await viewModel.StopLibraryFolderChangeAsync();

        // the stop can come before or after Borea saved the new folder, and either way the library is whole in one place
        Assert.False(viewModel.IsChangingLibraryFolder);
        Assert.Equal(viewModel.LibraryFolder == _library, Directory.Exists(Path.Combine(_library, "Instances")));
        Assert.Equal("Alpha", Assert.Single(viewModel.Instances).Name);
        await change;
    }

    [Fact]
    public async Task ChangeLibraryFolder_WhileBoreaCloses_DoesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.StopInstallsAsync();

        await viewModel.ChangeLibraryFolderCommand.ExecuteAsync(_library);

        Assert.Equal(harness.Root, viewModel.LibraryFolder);
        Assert.False(Directory.Exists(_library));
    }

    private static async Task<DiscoverItem> AfcRowAsync(ViewModelHarness harness)
    {
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return harness.ViewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");
    }

    private static bool IsAfcDownload(Uri uri) => uri.AbsolutePath.EndsWith("/AdvancedFlightComputer.zip", StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_library))
            Directory.Delete(_library, recursive: true);
    }
}
