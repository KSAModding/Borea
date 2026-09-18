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

    public void Dispose()
    {
        if (Directory.Exists(_library))
            Directory.Delete(_library, recursive: true);
    }
}
