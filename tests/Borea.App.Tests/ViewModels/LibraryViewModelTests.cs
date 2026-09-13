using Borea.Core.Instances;

namespace Borea.App.Tests.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task LoadAsync_NoInstances_ShowsTheEmptyStates()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.Empty(viewModel.Instances);
        Assert.Null(viewModel.ActiveInstance);
        Assert.False(viewModel.HasActiveInstance);
        Assert.Null(viewModel.InstalledVersionText);
    }

    [Fact]
    public async Task CreateInstance_FromTheDialog_AddsARowAndClosesTheDialog()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.NewInstanceName = "  Career  ";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Instances);
        Assert.Equal("Career", row.Name);
        Assert.Equal(harness.Localization.HomeInstanceSourceCustom, row.SourceText);
        Assert.False(viewModel.IsCreatingInstance);
        Assert.Equal(string.Empty, viewModel.NewInstanceName);
        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task CreateInstance_BlankName_DoesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.NewInstanceName = "   ";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);
        viewModel.CancelCreateInstanceCommand.Execute(null);

        Assert.Empty(viewModel.Instances);
        Assert.False(viewModel.IsCreatingInstance);
    }

    [Fact]
    public async Task CreateInstance_TakenName_ReportsTheRepositoryError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.NewInstanceName = "Career";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);
        viewModel.NewInstanceName = "career";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Instances);
        Assert.NotNull(viewModel.InstanceError);
    }

    [Fact]
    public async Task Activate_MarksTheRowActiveAndFillsTheCurrentInstallCard()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.Services.Instances.CreateAsync("Beta", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        await viewModel.Instances.Single(row => row.Name == "Beta").ActivateCommand.ExecuteAsync(null);

        Assert.Equal(["Alpha", "Beta"], viewModel.Instances.Select(row => row.Name));
        Assert.True(viewModel.Instances.Single(row => row.Name == "Beta").IsActive);
        Assert.False(viewModel.Instances.Single(row => row.Name == "Alpha").IsActive);
        Assert.Equal("Beta", viewModel.ActiveInstance?.Name);
        Assert.True(viewModel.HasActiveInstance);
        Assert.Equal(0, viewModel.ActiveInstance?.ModCount);
    }

    [Fact]
    public async Task ToggleActive_SwitchedOffOnTheActiveRow_LeavesNoInstanceActive()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().ToggleActiveCommand.ExecuteAsync(null);
        Assert.True(viewModel.Instances.Single().IsActive);

        await viewModel.Instances.Single().ToggleActiveCommand.ExecuteAsync(null);

        Assert.False(viewModel.Instances.Single().IsActive);
        Assert.Null(viewModel.ActiveInstance);
        Assert.False(viewModel.HasActiveInstance);
        Assert.Null(await harness.Services.Instances.GetActiveInstanceIdAsync());
        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task Rename_CommitsTheTrimmedName()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var row = Assert.Single(viewModel.Instances);

        row.BeginRenameCommand.Execute(null);
        Assert.True(row.IsRenaming);
        Assert.Equal("Alpha", row.EditName);
        row.EditName = " Gamma ";
        await row.CommitRenameCommand.ExecuteAsync(null);

        Assert.Equal("Gamma", Assert.Single(viewModel.Instances).Name);
    }

    [Fact]
    public async Task Rename_UnchangedOrCancelled_KeepsTheName()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var row = Assert.Single(viewModel.Instances);

        row.BeginRenameCommand.Execute(null);
        await row.CommitRenameCommand.ExecuteAsync(null);
        row.BeginRenameCommand.Execute(null);
        row.EditName = "Other";
        row.CancelCommand.Execute(null);

        Assert.False(row.IsRenaming);
        Assert.Equal("Alpha", (await harness.Services.Instances.GetAllAsync()).Single().Name);
    }

    [Fact]
    public async Task Delete_NeedsConfirmationThenRemovesTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var row = Assert.Single(viewModel.Instances);

        row.BeginRenameCommand.Execute(null);
        row.BeginDeleteCommand.Execute(null);
        Assert.False(row.IsRenaming);
        Assert.True(row.IsConfirmingDelete);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Instances);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task Navigation_OnlyOnePageIsVisible()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.SetMainWindowLibraryCommand.Execute(null);
        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.True(viewModel.IsLibrarySection);
        Assert.False(viewModel.CurrentWindowHome);

        viewModel.SetMainWindowTasksCommand.Execute(null);
        Assert.True(viewModel.CurrentWindowTasks);
        Assert.False(viewModel.IsLibrarySection);

        viewModel.SetMainWindowSettingsCommand.Execute(null);
        Assert.True(viewModel.IsSettingsOpen);
        Assert.True(viewModel.CurrentWindowTasks);
        viewModel.CloseSettingsCommand.Execute(null);
        Assert.False(viewModel.IsSettingsOpen);

        viewModel.SetMainWindowHomeCommand.Execute(null);
        Assert.True(viewModel.CurrentWindowHome);
        Assert.False(viewModel.CurrentWindowTasks);
    }

    [Fact]
    public async Task UnexpectedError_CanBeDismissed()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.UnexpectedError = "boom";
        viewModel.DismissUnexpectedErrorCommand.Execute(null);

        Assert.Null(viewModel.UnexpectedError);
    }
}
