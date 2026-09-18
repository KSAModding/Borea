using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class LaunchArgumentsViewModelTests
{
    [Fact]
    public async Task Modal_ShowsTheSplitAndSavesTheList()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        Assert.Single(viewModel.Instances).BeginEditLaunchArgumentsCommand.Execute(null);
        Assert.True(viewModel.IsLaunchArgumentsModalOpen);
        Assert.Equal(string.Empty, viewModel.ModalLaunchArguments);
        Assert.False(viewModel.HasModalLaunchArguments);

        viewModel.ModalLaunchArguments = "-windowed \"a b\" \"\"";
        Assert.Equal(["-windowed", "a b", "\"\""], viewModel.ModalLaunchArgumentItems);
        await viewModel.SaveLaunchArgumentsCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLaunchArgumentsModalOpen);
        Assert.Null(viewModel.InstanceError);
        var saved = Assert.Single(await harness.Services.Instances.GetAllAsync());
        Assert.Equal(["-windowed", "a b", ""], saved.LaunchArguments);

        Assert.Single(viewModel.Instances).BeginEditLaunchArgumentsCommand.Execute(null);
        Assert.Equal("-windowed \"a b\" \"\"", viewModel.ModalLaunchArguments);
    }

    [Fact]
    public async Task Modal_Cancelled_KeepsTheSavedArguments()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved =>
        {
            saved.SetLaunchArguments(["-windowed"]);
            return true;
        });
        await viewModel.LoadAsync();

        Assert.Single(viewModel.Instances).BeginEditLaunchArgumentsCommand.Execute(null);
        viewModel.ModalLaunchArguments = "-other";
        viewModel.CancelLaunchArgumentsModalCommand.Execute(null);

        Assert.False(viewModel.IsLaunchArgumentsModalOpen);
        Assert.Equal(["-windowed"], Assert.Single(await harness.Services.Instances.GetAllAsync()).LaunchArguments);
    }

    [Fact]
    public async Task Modal_HandoverFlagOfTheLoader_KeepsTheModalOpenWithTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync(RecordStarMapAsync);
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        Assert.Single(viewModel.Instances).BeginEditLaunchArgumentsCommand.Execute(null);
        viewModel.ModalLaunchArguments = "-instancepath D:/Other";
        await viewModel.SaveLaunchArgumentsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLaunchArgumentsModalOpen);
        Assert.Equal(harness.Localization.FormatLaunchArgumentsHandoverFlag("StarMap", "-instancepath"), viewModel.InstanceError);
        Assert.Empty(Assert.Single(await harness.Services.Instances.GetAllAsync()).LaunchArguments);
    }

    [Fact]
    public async Task Modal_ListingOfTheLoaderCannotBeRead_SavesTheArguments()
    {
        using var harness = await ViewModelHarness.CreateAsync(RecordStarMapAsync, indexOffline: true);
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        Assert.Single(viewModel.Instances).BeginEditLaunchArgumentsCommand.Execute(null);
        viewModel.ModalLaunchArguments = "-instancepath D:/Other";
        await viewModel.SaveLaunchArgumentsCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLaunchArgumentsModalOpen);
        Assert.Null(viewModel.InstanceError);
        Assert.Equal(["-instancepath", "D:/Other"], Assert.Single(await harness.Services.Instances.GetAllAsync()).LaunchArguments);
    }

    private static Task RecordStarMapAsync(BoreaServices services)
    {
        var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;
        return services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
            "StarMap",
            new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
    }
}
