using System.ComponentModel;
using Borea.App.ViewModels;
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
        Assert.True(viewModel.IsNameModalOpen);
        Assert.Equal(harness.Localization.ModalCreateInstanceTitle, viewModel.NameModalTitle);
        viewModel.ModalInstanceName = "  Career  ";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Instances);
        Assert.Equal("Career", row.Name);
        Assert.Equal(harness.Localization.HomeInstanceSourceCustom, row.SourceText);
        Assert.False(viewModel.IsCreatingInstance);
        Assert.Equal(string.Empty, viewModel.ModalInstanceName);
        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task CreateInstance_NoActiveInstance_ShowsTheNewInstanceAsActive()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.ModalInstanceName = "Career";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Instances);
        Assert.True(row.IsActive);
        Assert.Same(row, viewModel.ActiveInstance);
        Assert.True(viewModel.HasActiveInstance);
    }

    [Fact]
    public async Task CreateInstance_AnotherInstanceIsActive_KeepsItActive()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.ModalInstanceName = "Beta";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.False(viewModel.Instances.Single(row => row.Name == "Beta").IsActive);
        Assert.Equal("Alpha", viewModel.ActiveInstance?.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateInstance_BlankName_KeepsTheModalOpenWithTheMessage(string name)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.ModalInstanceName = name;
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCreatingInstance);
        Assert.Equal(harness.Localization.ModalNameRequired, viewModel.InstanceError);
        Assert.Empty(viewModel.Instances);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task NameRequiredMessage_NameChanges_GoesAway()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.InstanceError);
        viewModel.ModalInstanceName = "C";

        Assert.Null(viewModel.InstanceError);
        Assert.True(viewModel.IsCreatingInstance);
    }

    [Fact]
    public async Task NameRequiredMessage_ModalClosedAndOpenedAgain_GoesAway()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.InstanceError);
        viewModel.CancelNameModalCommand.Execute(null);
        Assert.Null(viewModel.InstanceError);
        viewModel.BeginCreateInstanceCommand.Execute(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task NameRequiredMessage_LanguageChangedWhileShown_StillGoesAway()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        harness.Localization.TrySetCulture("de");
        viewModel.CancelNameModalCommand.Execute(null);

        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task CreateInstance_TakenName_KeepsTheModalOpenWithTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.ModalInstanceName = "Career";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);
        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.ModalInstanceName = "career";
        await viewModel.CreateInstanceCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Instances);
        Assert.True(viewModel.IsNameModalOpen);
        Assert.Equal(harness.Localization.ModalNameTaken, viewModel.InstanceError);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task Rename_ModalClosedBeforeTheResult_ShowsTheErrorAsAToast()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.Services.Instances.CreateAsync("Beta", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        await viewModel.RenameInstanceAsync(viewModel.Instances.Single(item => item.Name == "Alpha"), "beta");

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.FormatToastRenameFailed("Alpha"), toast.Message);
        Assert.NotNull(toast.Detail);
        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task BeginCreateInstance_AfterARefusedName_OpensWithoutTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Career", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        viewModel.BeginCreateInstanceCommand.Execute(null);
        viewModel.ModalInstanceName = "career";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        viewModel.CancelNameModalCommand.Execute(null);
        Assert.NotNull(viewModel.InstanceError);
        viewModel.BeginCreateInstanceCommand.Execute(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.Null(viewModel.InstanceError);
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

        Assert.True(viewModel.Instances.Single().IsActive);

        await viewModel.Instances.Single().ToggleActiveCommand.ExecuteAsync(null);

        Assert.False(viewModel.Instances.Single().IsActive);
        Assert.Null(viewModel.ActiveInstance);
        Assert.False(viewModel.HasActiveInstance);
        Assert.Null(await harness.Services.Instances.GetActiveInstanceIdAsync());
        Assert.Null(viewModel.InstanceError);
    }

    [Fact]
    public async Task ToggleActive_MovesTheRowsAboveAndBelowTheActiveHeadingAtOnce()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.Services.Instances.CreateAsync("Beta", InstanceSource.Custom.Value);
        await harness.Services.Instances.CreateAsync("Gamma", InstanceSource.Custom.Value);
        // the first new instance became active, and this test starts without one
        await harness.Services.Instances.ClearActiveInstanceAsync();
        await viewModel.LoadAsync();
        Assert.Equal(["Alpha", "Beta", "Gamma"], viewModel.OtherInstances.Select(row => row.Name));

        await viewModel.OtherInstances.Single(row => row.Name == "Gamma").ToggleActiveCommand.ExecuteAsync(null);

        Assert.Equal("Gamma", viewModel.ActiveInstance?.Name);
        Assert.Equal(["Alpha", "Beta"], viewModel.OtherInstances.Select(row => row.Name));

        await viewModel.OtherInstances.Single(row => row.Name == "Beta").ToggleActiveCommand.ExecuteAsync(null);

        Assert.Equal("Beta", viewModel.ActiveInstance?.Name);
        Assert.Equal(["Alpha", "Gamma"], viewModel.OtherInstances.Select(row => row.Name));

        await viewModel.ActiveInstance!.ToggleActiveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ActiveInstance);
        Assert.Equal(["Alpha", "Beta", "Gamma"], viewModel.OtherInstances.Select(row => row.Name));
    }

    [Fact]
    public async Task Rename_FromTheModal_SavesTheTrimmedNameAndClosesTheModal()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var row = Assert.Single(viewModel.Instances);

        row.BeginRenameCommand.Execute(null);
        Assert.True(viewModel.IsNameModalOpen);
        Assert.Same(row, viewModel.RenamingInstance);
        Assert.Equal("Alpha", viewModel.ModalInstanceName);
        Assert.Equal(harness.Localization.ModalRenameInstanceTitle, viewModel.NameModalTitle);
        Assert.Equal(harness.Localization.LibrarySave, viewModel.NameModalConfirmText);
        viewModel.ModalInstanceName = " Gamma ";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Equal("Gamma", Assert.Single(viewModel.Instances).Name);
        Assert.False(viewModel.IsNameModalOpen);
        Assert.Null(viewModel.RenamingInstance);
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
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsNameModalOpen);
        row.BeginRenameCommand.Execute(null);
        viewModel.ModalInstanceName = "Other";
        viewModel.CancelNameModalCommand.Execute(null);

        Assert.False(viewModel.IsNameModalOpen);
        Assert.Equal("Alpha", (await harness.Services.Instances.GetAllAsync()).Single().Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_BlankName_KeepsTheModalOpenWithTheMessage(string name)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var row = Assert.Single(viewModel.Instances);

        row.BeginRenameCommand.Execute(null);
        viewModel.ModalInstanceName = name;
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.Same(row, viewModel.RenamingInstance);
        Assert.Equal(harness.Localization.ModalNameRequired, viewModel.InstanceError);
        Assert.Equal("Alpha", Assert.Single(await harness.Services.Instances.GetAllAsync()).Name);
    }

    [Fact]
    public async Task Rename_TakenName_KeepsTheModalOpenWithTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await harness.Services.Instances.CreateAsync("Beta", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        viewModel.Instances.Single(row => row.Name == "Beta").BeginRenameCommand.Execute(null);
        viewModel.ModalInstanceName = "alpha";
        await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsNameModalOpen);
        Assert.NotNull(viewModel.InstanceError);
        Assert.Equal(["Alpha", "Beta"], (await harness.Services.Instances.GetAllAsync()).Select(instance => instance.Name).Order());
    }

    [Fact]
    public async Task OpenFolder_CreatesAndOpensTheInstanceFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value)).Instance;
        await viewModel.LoadAsync();
        viewModel.SetMainWindowLibraryCommand.Execute(null);
        string? opened = null;
        viewModel.OpenWithSystem = target => opened = target;

        Assert.Single(viewModel.Instances).OpenFolderCommand.Execute(null);

        var root = harness.Services.Paths.GetInstanceRoot(instance.InstanceId);
        Assert.Equal(root, opened);
        Assert.True(Directory.Exists(root));
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Fact]
    public async Task OpenFolder_SystemRefuses_NamesTheInstanceInAnErrorToast()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        viewModel.OpenWithSystem = _ => throw new Win32Exception("No application is associated with the folder.");

        Assert.Single(viewModel.Instances).OpenFolderCommand.Execute(null);

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.True(toast.IsFailed);
        Assert.Equal(harness.Localization.FormatToastOpenFailed("Alpha"), toast.Message);
        Assert.Equal("No application is associated with the folder.", toast.Detail);
    }

    [Fact]
    public async Task Delete_AsksInTheModalThenRemovesTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var row = Assert.Single(viewModel.Instances);

        row.BeginDeleteCommand.Execute(null);
        Assert.True(viewModel.IsDeleteModalOpen);
        Assert.Equal(harness.Localization.FormatModalDeleteInstanceText("Alpha"), viewModel.DeleteModalText);
        await viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDeleteModalOpen);
        Assert.Empty(viewModel.Instances);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task Delete_CancelInTheModal_KeepsTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);
        viewModel.CancelDeleteModalCommand.Execute(null);
        await viewModel.ConfirmDeleteModalCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDeleteModalOpen);
        Assert.Single(viewModel.Instances);
        Assert.Single(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task Load_NeverPlayed_SaysSoWithoutADate()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        var row = Assert.Single(viewModel.Instances);

        Assert.Null(row.LastPlayedAt);
        Assert.Equal(harness.Localization.LibraryNeverPlayed, row.LastPlayedText);
        Assert.Null(row.LastPlayedToolTip);
    }

    [Fact]
    public async Task Load_NamesTheModCountInItsToolTip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        var row = Assert.Single(viewModel.Instances);

        Assert.Equal("Installed mods: 0", row.ModCountToolTip);
    }

    [Fact]
    public async Task Load_GameLogNewerThanTheRecordedLaunch_ShowsTheLogTime()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value)).Instance;
        await RecordPlayedAsync(harness, instance.InstanceId, DateTimeOffset.UtcNow.AddDays(-6));
        var logWrittenAt = DateTimeOffset.UtcNow.AddHours(-2);
        var log = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        await File.WriteAllTextAsync(log, "09:34:00.000  INFO loaded settings from settings.toml\n");
        File.SetLastWriteTimeUtc(log, logWrittenAt.UtcDateTime);
        await viewModel.LoadAsync();

        var row = Assert.Single(viewModel.Instances);

        Assert.NotNull(row.LastPlayedAt);
        Assert.Equal(logWrittenAt.UtcDateTime, row.LastPlayedAt.Value.UtcDateTime, TimeSpan.FromSeconds(1));
        Assert.Equal(harness.Localization.FormatTimeAgoShort(TimeSpan.FromHours(2)), row.LastPlayedText);
        Assert.Equal(harness.Localization.FormatLibraryLastPlayed(MainViewModel.DateTimeText(row.LastPlayedAt.Value)), row.LastPlayedToolTip);
    }

    [Fact]
    public async Task SortByLastPlayed_PutsTheNewestFirstAndTheNeverPlayedLast()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        var beta = (await harness.Services.Instances.CreateAsync("Beta", InstanceSource.Custom.Value)).Instance;
        var gamma = (await harness.Services.Instances.CreateAsync("Gamma", InstanceSource.Custom.Value)).Instance;
        await RecordPlayedAsync(harness, beta.InstanceId, DateTimeOffset.UtcNow.AddDays(-6));
        await RecordPlayedAsync(harness, gamma.InstanceId, DateTimeOffset.UtcNow.AddHours(-1));
        await viewModel.LoadAsync();
        Assert.Equal(["Alpha", "Beta", "Gamma"], viewModel.Instances.Select(row => row.Name));

        viewModel.SelectLibrarySortCommand.Execute(LibrarySort.LastPlayed);
        Assert.Equal(["Gamma", "Beta", "Alpha"], viewModel.Instances.Select(row => row.Name));
        Assert.Equal(harness.Localization.LibrarySortLastPlayed, viewModel.LibrarySortText);

        await viewModel.LoadAsync();
        Assert.Equal(["Gamma", "Beta", "Alpha"], viewModel.Instances.Select(row => row.Name));
    }

    [Fact]
    public async Task Sort_KeepsTheActiveInstanceFirstAndTheOthersInOrder()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        var beta = (await harness.Services.Instances.CreateAsync("Beta", InstanceSource.Custom.Value)).Instance;
        var gamma = (await harness.Services.Instances.CreateAsync("Gamma", InstanceSource.Custom.Value)).Instance;
        await RecordPlayedAsync(harness, beta.InstanceId, DateTimeOffset.UtcNow.AddDays(-6));
        await RecordPlayedAsync(harness, gamma.InstanceId, DateTimeOffset.UtcNow.AddHours(-1));
        await harness.Services.Instances.SetActiveInstanceAsync(beta.InstanceId);
        await viewModel.LoadAsync();
        Assert.Equal("Beta", viewModel.ActiveInstance?.Name);
        Assert.Equal(["Alpha", "Gamma"], viewModel.OtherInstances.Select(row => row.Name));

        viewModel.SelectLibrarySortCommand.Execute(LibrarySort.LastPlayed);
        Assert.Equal(["Gamma", "Alpha"], viewModel.OtherInstances.Select(row => row.Name));

        await viewModel.LoadAsync();
        Assert.Equal("Beta", viewModel.ActiveInstance?.Name);
        Assert.Equal(["Gamma", "Alpha"], viewModel.OtherInstances.Select(row => row.Name));

        viewModel.SelectLibrarySortCommand.Execute(LibrarySort.Name);
        Assert.Equal(["Alpha", "Gamma"], viewModel.OtherInstances.Select(row => row.Name));
    }

    private static Task RecordPlayedAsync(ViewModelHarness harness, Guid instanceId, DateTimeOffset playedAt) =>
        harness.Services.Instances.UpdateAsync(instanceId, instance =>
        {
            instance.RecordPlayed(playedAt);
            return true;
        });

    [Fact]
    public async Task Navigation_OnlyOnePageIsVisible()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.SetMainWindowLibraryCommand.Execute(null);
        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.True(viewModel.IsLibrarySection);
        Assert.False(viewModel.CurrentWindowHome);

        viewModel.ToggleTasksCommand.Execute(null);
        Assert.True(viewModel.IsTasksOpen);
        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.True(viewModel.IsLibrarySection);

        viewModel.SetMainWindowSettingsCommand.Execute(null);
        Assert.True(viewModel.IsSettingsOpen);
        Assert.False(viewModel.IsTasksOpen);
        viewModel.CloseSettingsCommand.Execute(null);
        Assert.False(viewModel.IsSettingsOpen);
        Assert.True(viewModel.CurrentWindowLibrary);

        viewModel.ToggleTasksCommand.Execute(null);
        viewModel.SetMainWindowHomeCommand.Execute(null);
        Assert.True(viewModel.CurrentWindowHome);
        Assert.False(viewModel.IsTasksOpen);
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
