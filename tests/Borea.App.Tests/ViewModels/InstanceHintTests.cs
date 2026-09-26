using Borea.App.ViewModels;
using Borea.Core.History;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_WithoutActiveInstance_ShowsTheHintToast_AndStartsNoTask(bool hasInstances)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        if (hasInstances)
            await LeaveOneInactiveInstanceAsync(harness);
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.First();

        await item.InstallCommand.ExecuteAsync(null);

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(hasInstances ? harness.Localization.DiscoverNoActiveInstance : harness.Localization.DiscoverNoInstance, toast.Message);
        Assert.True(toast.IsFailed);
        Assert.True(toast.HasAction);
        Assert.Equal(harness.Localization.DiscoverOpenLibrary, toast.ActionText);
        Assert.False(toast.CanShowDetails);
        Assert.DoesNotContain(viewModel.Tasks.Running.Concat(viewModel.Tasks.History), task => task.Kind is TaskKind.ModInstall or TaskKind.PackInstall);
        Assert.False(item.IsInstalling);
        Assert.Null(item.InstallError);
        Assert.Null(item.InstallWarning);
    }

    [Theory]
    [InlineData("mod page")]
    [InlineData("mod versions")]
    [InlineData("modpacks")]
    [InlineData("pack page")]
    [InlineData("pack versions")]
    public async Task EveryAdd_WithoutActiveInstance_ShowsTheHintToast(string where)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("tools-pack", "Tools Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.10")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var listing = viewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools");
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        var pack = Assert.Single(viewModel.DiscoverPacks);

        switch (where)
        {
            case "mod page":
                await listing.OpenCommand.ExecuteAsync(null);
                await viewModel.SelectedContent!.InstallCommand.ExecuteAsync(null);
                break;
            case "mod versions":
                await listing.OpenCommand.ExecuteAsync(null);
                await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
                await viewModel.ContentVersions.First().InstallCommand.ExecuteAsync(null);
                break;
            case "modpacks":
                await pack.InstallCommand.ExecuteAsync(null);
                break;
            case "pack page":
                await pack.OpenCommand.ExecuteAsync(null);
                await viewModel.SelectedPack!.InstallCommand.ExecuteAsync(null);
                break;
            default:
                await pack.OpenCommand.ExecuteAsync(null);
                viewModel.ShowPackVersionsCommand.Execute(null);
                await viewModel.PackVersions.Single().InstallCommand.ExecuteAsync(null);
                break;
        }

        Assert.Equal(harness.Localization.DiscoverNoInstance, Assert.Single(viewModel.Toasts.Items).Message);
        Assert.DoesNotContain(viewModel.Tasks.Running.Concat(viewModel.Tasks.History), task => task.Kind is TaskKind.ModInstall or TaskKind.PackInstall);
        Assert.False(pack.IsInstalling);
        Assert.Null(pack.InstallError);
    }

    [Fact]
    public async Task HintToast_ActionOpensTheLibrary()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        await viewModel.DiscoverItems.First().InstallCommand.ExecuteAsync(null);
        var toast = Assert.Single(viewModel.Toasts.Items);

        toast.RunActionCommand.Execute(null);

        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.False(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.IsNameModalOpen);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task HintToast_FollowsTheLanguage()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.First().InstallCommand.ExecuteAsync(null);
        var toast = Assert.Single(viewModel.Toasts.Items);
        var english = (toast.Message, toast.ActionText);
        var changed = new List<string?>();
        toast.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.NotEqual(english, (toast.Message, toast.ActionText));
        Assert.Equal(harness.Localization.DiscoverNoInstance, toast.Message);
        Assert.Equal(harness.Localization.DiscoverOpenLibrary, toast.ActionText);
        Assert.Contains(nameof(ToastItem.Message), changed);
        Assert.Contains(nameof(ToastItem.ActionText), changed);
    }

    [Fact]
    public async Task Add_WithActiveInstance_PlansTheInstallAndShowsNoToast()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");

        await item.InstallCommand.ExecuteAsync(null);

        // without a game the compatibility is unknown, so the plan waits on the row for a confirmation
        Assert.NotNull(item.InstallWarning);
        Assert.NotNull(item.PendingPlan);
        Assert.Empty(viewModel.Toasts.Items);
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
        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);
        await viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);
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

    [Fact]
    public async Task ActiveInstance_IsNamedByTheLineAndTheAddTooltip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);

        Assert.Equal("Adding to Alpha", viewModel.AddingToText);
        Assert.Equal("Add to Alpha", viewModel.AddToText);
        Assert.Equal("Adding to ", viewModel.AddingToBefore);
        Assert.Equal("", viewModel.AddingToAfter);
    }

    [Fact]
    public async Task NoActiveInstance_HidesTheLine()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await LeaveOneInactiveInstanceAsync(harness);

        Assert.False(viewModel.HasActiveInstance);
        Assert.Null(viewModel.AddingToText);
        Assert.Equal(harness.Localization.DiscoverAdd, viewModel.AddToText);
    }

    [Fact]
    public async Task ActivateAndRename_UpdateTheLineAndTheAddTooltip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.Services.Instances.CreateAsync("Beta", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single(item => item.Name == "Alpha").ActivateCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await viewModel.Instances.Single(item => item.Name == "Beta").ActivateCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatDiscoverAddingTo("Beta"), viewModel.AddingToText);
        Assert.Equal(harness.Localization.FormatDiscoverAddTo("Beta"), viewModel.AddToText);
        Assert.Contains(nameof(MainViewModel.AddingToText), changed);
        Assert.Contains(nameof(MainViewModel.AddToText), changed);

        changed.Clear();
        await viewModel.RenameInstanceAsync(viewModel.ActiveInstance!, "Career");

        Assert.Equal(harness.Localization.FormatDiscoverAddingTo("Career"), viewModel.AddingToText);
        Assert.Equal(harness.Localization.FormatDiscoverAddTo("Career"), viewModel.AddToText);
        Assert.Contains(nameof(MainViewModel.AddingToText), changed);
        Assert.Contains(nameof(MainViewModel.AddToText), changed);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesTheLineAndTheAddTooltip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        var english = (viewModel.AddingToText, viewModel.AddToText);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.NotEqual(english, (viewModel.AddingToText, viewModel.AddToText));
        Assert.Equal(harness.Localization.FormatDiscoverAddingTo("Alpha"), viewModel.AddingToText);
        Assert.Equal(harness.Localization.FormatDiscoverAddTo("Alpha"), viewModel.AddToText);
        Assert.Equal(harness.Localization.SplitDiscoverAddingTo().Before, viewModel.AddingToBefore);
        Assert.Contains(nameof(MainViewModel.AddingToText), changed);
        Assert.Contains(nameof(MainViewModel.AddingToBefore), changed);
        Assert.Contains(nameof(MainViewModel.AddToText), changed);
    }

    private static async Task LeaveOneInactiveInstanceAsync(ViewModelHarness harness)
    {
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        await harness.ViewModel.ActiveInstance!.ToggleActiveCommand.ExecuteAsync(null);
    }
}
