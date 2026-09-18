using Borea.App.ViewModels;
using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class InstanceHintTests
{
    [Fact]
    public async Task Home_NoInstance_OpensTheNewInstanceModal()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.False(viewModel.HasActiveInstance);
        Assert.False(viewModel.HasInstances);
        Assert.Equal(harness.Localization.HomeNoInstance, viewModel.HomeInstanceHintText);
        Assert.Equal(harness.Localization.DiscoverCreateInstance, viewModel.InstanceHintActionText);

        viewModel.FollowInstanceHintCommand.Execute(null);

        Assert.True(viewModel.IsCreatingInstance);
        Assert.Equal(harness.Localization.ModalCreateInstanceTitle, viewModel.NameModalTitle);
        Assert.True(viewModel.CurrentWindowHome);
    }

    [Fact]
    public async Task Home_InstancesButNoneActive_OpensTheLibrary()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await LeaveOneInactiveInstanceAsync(harness);

        Assert.False(viewModel.HasActiveInstance);
        Assert.True(viewModel.HasInstances);
        Assert.Equal(harness.Localization.HomeNoActiveInstance, viewModel.HomeInstanceHintText);
        Assert.Equal(harness.Localization.DiscoverOpenLibrary, viewModel.InstanceHintActionText);

        viewModel.FollowInstanceHintCommand.Execute(null);

        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.False(viewModel.CurrentWindowHome);
        Assert.False(viewModel.IsNameModalOpen);
    }

    [Fact]
    public async Task Discover_NoInstance_OpensTheNewInstanceModal()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscoverCommand.Execute(null);

        Assert.Equal(harness.Localization.DiscoverNoInstance, viewModel.InstanceHintText);
        Assert.Equal(harness.Localization.DiscoverCreateInstance, viewModel.InstanceHintActionText);

        viewModel.FollowInstanceHintCommand.Execute(null);

        Assert.True(viewModel.IsCreatingInstance);
        Assert.True(viewModel.CurrentWindowDiscover);
    }

    [Fact]
    public async Task Discover_InstancesButNoneActive_OpensTheLibrary()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await LeaveOneInactiveInstanceAsync(harness);
        viewModel.SetMainWindowDiscoverCommand.Execute(null);

        Assert.Equal(harness.Localization.DiscoverNoActiveInstance, viewModel.InstanceHintText);
        Assert.Equal(harness.Localization.DiscoverOpenLibrary, viewModel.InstanceHintActionText);

        viewModel.FollowInstanceHintCommand.Execute(null);

        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.False(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.IsNameModalOpen);
    }

    [Fact]
    public async Task CreateInstance_FromTheHint_ChangesTheHintToTheLibrary()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.FollowInstanceHintCommand.Execute(null);
        viewModel.ModalInstanceName = "Career";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasInstances);
        Assert.Equal(harness.Localization.HomeNoActiveInstance, viewModel.HomeInstanceHintText);
        Assert.Equal(harness.Localization.DiscoverNoActiveInstance, viewModel.InstanceHintText);
        Assert.Equal(harness.Localization.DiscoverOpenLibrary, viewModel.InstanceHintActionText);
        Assert.Contains(nameof(MainViewModel.HomeInstanceHintText), changed);
        Assert.Contains(nameof(MainViewModel.InstanceHintText), changed);
        Assert.Contains(nameof(MainViewModel.InstanceHintActionText), changed);
    }

    [Fact]
    public async Task ActivateDeactivateAndDelete_UpdateTheHint()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasActiveInstance);
        Assert.Contains(nameof(MainViewModel.HasActiveInstance), changed);

        await viewModel.ActiveInstance!.ToggleActiveCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasActiveInstance);
        Assert.Equal(harness.Localization.HomeNoActiveInstance, viewModel.HomeInstanceHintText);

        changed.Clear();
        await viewModel.Instances.Single().ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasInstances);
        Assert.Equal(harness.Localization.HomeNoInstance, viewModel.HomeInstanceHintText);
        Assert.Equal(harness.Localization.DiscoverNoInstance, viewModel.InstanceHintText);
        Assert.Equal(harness.Localization.DiscoverCreateInstance, viewModel.InstanceHintActionText);
        Assert.Contains(nameof(MainViewModel.HomeInstanceHintText), changed);
        Assert.Contains(nameof(MainViewModel.InstanceHintText), changed);
        Assert.Contains(nameof(MainViewModel.InstanceHintActionText), changed);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesTheHint()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await LeaveOneInactiveInstanceAsync(harness);
        var english = viewModel.InstanceHintText;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.NotEqual(english, viewModel.InstanceHintText);
        Assert.Equal(harness.Localization.HomeNoActiveInstance, viewModel.HomeInstanceHintText);
        Assert.Equal(harness.Localization.DiscoverNoActiveInstance, viewModel.InstanceHintText);
        Assert.Equal(harness.Localization.DiscoverOpenLibrary, viewModel.InstanceHintActionText);
        Assert.Contains(nameof(MainViewModel.HomeInstanceHintText), changed);
        Assert.Contains(nameof(MainViewModel.InstanceHintText), changed);
        Assert.Contains(nameof(MainViewModel.InstanceHintActionText), changed);
    }

    private static async Task LeaveOneInactiveInstanceAsync(ViewModelHarness harness)
    {
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        await harness.ViewModel.ActiveInstance!.ToggleActiveCommand.ExecuteAsync(null);
    }
}
