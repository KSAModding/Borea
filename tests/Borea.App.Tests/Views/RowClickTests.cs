using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views.Pages;
using Borea.Core.Dependencies;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.App.Tests.Views;

/// <summary>
/// The click targets of the three content rows. A click beside the controls
/// opens the page of the row, every control inside the row keeps its own job,
/// and a control which is off answers nothing at all.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RowClickTests
{
    private const string ModId = "AdvancedFlightComputer";

    private static Task<T> OnPageAsync<T>(ViewModelHarness harness, Func<Control> createPage, Func<Window, Control, Task<T>> read) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var page = createPage();
            var window = new Window { Width = 1200, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return await read(window, page);
            }
            finally
            {
                window.Close();
            }
        });

    private static void Click(Window window, Visual target, Point inTarget)
    {
        var point = target.TranslatePoint(inTarget, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void ClickCenter(Window window, Visual target)
        => Click(window, target, new Point(target.Bounds.Width / 2, target.Bounds.Height / 2));

    /// <summary>
    /// Clicks the padding on the left of the row. The padding lies outside every
    /// control the row carries, so only the row itself can answer the click, and
    /// before this change nothing there was clickable at all.
    /// </summary>
    private static void ClickThePadding(Window window, Button row)
    {
        var inRow = new Point(4, row.Bounds.Height / 2);
        foreach (var control in row.GetVisualDescendants().Where(visual => visual.IsEffectivelyVisible
            && visual is Button or ToggleSwitch or CheckBox or RadioButton or MenuItem))
        {
            if (control.TranslatePoint(new Point(0, 0), row) is { } corner)
                Assert.False(new Rect(corner, control.Bounds.Size).Contains(inRow), $"{control.GetType().Name} covers the padding.");
        }

        Click(window, row, inRow);
    }

    /// <summary>The button around a whole row, which carries the open command of that row.</summary>
    private static Button Row(Control page, ICommand open)
        => page.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("card-button") && ReferenceEquals(button.Command, open));

    private static Button Inside(Visual row, ICommand command)
        => row.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, command));

    private static Button Shown(Visual row, string content)
        => row.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Content as string == content);

    /// <summary>A run that reports no install, so that a click on Pause or on Stop is the only thing that moves it.</summary>
    private static InstallRun NewRun(ViewModelHarness harness)
    {
        var registry = new TaskRegistry(harness.Localization, () => null, () => null, _ => Task.CompletedTask);
        return new InstallRun(harness.Localization, registry.Start(TaskKind.ModInstall, null, null, null, null, null, TaskState.Running));
    }

    /// <summary>
    /// One recommendation to tick and one group of alternatives with nothing
    /// picked, so the choices carry a check box and radio buttons and the
    /// primary button of the row stays off until the group has an answer.
    /// </summary>
    private static InstallChoices OpenChoices(Guid instanceId)
    {
        var choices = new InstallChoices(instanceId, [], name => name);
        choices.Recommended.Add(new RecommendedChoice(choices, "recommended", "MeasureTools", isSelected: false));
        var dependency = ModDependency.OfAlternatives(
            ModDependencyKind.Required,
            [new ModDependencyAlternative("KSArmory"), new ModDependencyAlternative("MeasureTools")]);
        var alternatives = new PlanningChoice("group", ModId, PlanningChoiceKind.Alternative, ["KSArmory", "MeasureTools"], null, dependency);
        choices.Alternatives.Add(new AlternativeChoice(choices, alternatives, name => name));
        Assert.False(choices.IsComplete);
        return choices;
    }

    private static async Task<ViewModelHarness> InstanceAsync(ModInstallOwnership ownership = ModInstallOwnership.Borea)
    {
        var harness = await ViewModelHarness.CreateAsync();
        await InstalledContent.AddAsync(harness, ModId, activate: true, ownership: ownership, version: "0.7.4");
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        return harness;
    }

    /// <summary>An instance whose only mod the content index does not list, so its row has no page to link to.</summary>
    private static async Task<ViewModelHarness> InstanceWithAnUnlistedModAsync()
    {
        const string unlisted = "HandDropped";
        var harness = await ViewModelHarness.CreateAsync();
        var services = harness.Services;
        var instance = (await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;

        // the manifest only lists a mod whose folder holds a mod.toml
        var folder = Directory.CreateDirectory(Path.Combine(services.Paths.GetInstanceModsFolder(instance.InstanceId), unlisted));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{unlisted}\"");

        var release = new ModVersionMetadata(
            specVersion: 1,
            modId: unlisted,
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
            gameMin: "2026.9.7.5402",
            gameMinRevision: 5402,
            download: new DownloadInfo("https://example.invalid/hand-dropped.zip", new string('A', 64), null, "application/zip"),
            installSizeBytes: null,
            dependencies: []);
        instance.AddMod(new InstalledMod(unlisted, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release, ownership: ModInstallOwnership.Foreign));
        await services.Instances.SaveAsync(instance);
        await services.ModState.AddEntryAsync(instance.InstanceId, unlisted, enabled: true);
        await services.Instances.SetActiveInstanceAsync(instance.InstanceId);

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        return harness;
    }

    /// <summary>An instance whose mod another mod needs, so that the row links to the page but its trash stays off.</summary>
    private static async Task<ViewModelHarness> InstanceWithABlockedRemoveAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        var instance = await InstalledContent.AddAsync(harness, ModId, activate: true, ownership: ModInstallOwnership.Borea, version: "0.7.4");
        var dependent = new ForeignMod("FlightPlanner", [new LocalModDependency(ModId, optional: false)]);
        await harness.Services.Instances.SaveAsync(Instance.FromExisting(
            instance.InstanceId, instance.Name, instance.Source, instance.CreatedAt, instance.Mods.ToList(), [dependent], instance.IsFavorite));
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        return harness;
    }

    private static async Task<ViewModelHarness> DiscoverAsync(string? installed = null, Func<string, string>? editSnapshot = null)
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: editSnapshot);
        if (installed is null)
        {
            var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
            await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        }
        else
        {
            await InstalledContent.AddAsync(harness, installed, activate: true, ownership: ModInstallOwnership.Borea);
        }

        await harness.ViewModel.LoadAsync();
        harness.ViewModel.SetMainWindowDiscoverCommand.Execute(null);
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return harness;
    }

    private static async Task<ViewModelHarness> PacksAsync()
    {
        var pack = PackViewModelTests.Pack("tools-pack", "Tools Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.9")));
        var harness = await DiscoverAsync(editSnapshot: PackViewModelTests.WithPacks(pack));
        harness.ViewModel.ShowDiscoverModpacksCommand.Execute(null);
        return harness;
    }

    [Fact]
    public async Task ContentRow_ThePaddingBesideTheControls_OpensTheModPage()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();

        var (running, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickThePadding(window, row);
            var running = item.OpenCommand.ExecutionTask;
            if (running is not null)
                await running;
            return (running, viewModel.CurrentWindowContent, viewModel.SelectedContent?.ModId);
        });

        Assert.NotNull(running);
        Assert.True(opened);
        Assert.Equal(ModId, selected);
    }

    [Fact]
    public async Task ContentRow_Update_PlansTheUpdateAndLeavesTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        Assert.True(item.HasUpdate);

        var (confirming, cancelled, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, item.UpdateCommand));
            if (item.UpdateCommand.ExecutionTask is { } running)
                await running;

            var confirming = item.IsConfirmingUpdate;
            page.UpdateLayout();
            ClickCenter(window, Shown(row, harness.Localization.LibraryCancel));
            return (confirming, item.IsConfirmingUpdate, item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        // the update asked for a confirmation and the confirmation took it back, so both clicks reached their button
        Assert.True(confirming);
        Assert.False(cancelled);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_ConfirmTheUpdate_RunsItAndLeavesTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();

        var (confirmed, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, item.UpdateCommand));
            if (item.UpdateCommand.ExecutionTask is { } planning)
                await planning;

            Assert.True(item.IsConfirmingUpdate);
            page.UpdateLayout();
            ClickCenter(window, Shown(row, item.ConfirmUpdateText));
            if (item.ConfirmUpdateCommand.ExecutionTask is { } confirming)
                await confirming;

            return (item.ConfirmUpdateCommand.ExecutionTask, item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        // the update left the confirmation and ran, so the click reached the primary button and not the row
        Assert.NotNull(confirmed);
        Assert.False(item.IsConfirmingUpdate);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_PauseAndStop_ReachTheRunAndLeaveTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        var run = NewRun(harness);
        run.IsPaused = true;
        item.Run = run;
        item.IsInstalling = true;

        var (resumed, stopped, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, run.TogglePauseCommand));
            var resumed = !run.IsPaused;
            page.UpdateLayout();
            ClickCenter(window, Inside(row, run.StopCommand));
            return Task.FromResult((resumed, run.IsStopping, item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        // the run took the resume and then the stop, so both clicks reached their button
        Assert.True(resumed);
        Assert.True(stopped);
        Assert.True(run.InstallStop.IsRequested);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_TheSwitch_TurnsTheModOffAndLeavesTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        var instanceId = viewModel.SelectedInstance!.InstanceId;
        Assert.True(item.IsEnabled);

        var (opened, selected) = await OnPageAsync(harness, () => new InstancePage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, row.GetVisualDescendants().OfType<ToggleSwitch>().Single());
            if (item.ToggleEnabledCommand.ExecutionTask is { } running)
                await running;
            return (item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        // the mod really turned off, which is the proof that the click reached the switch
        Assert.False(item.IsEnabled);
        var entries = await harness.Services.ModState.GetEntriesAsync(instanceId);
        Assert.False(entries.Single(entry => entry.ModId == ModId).Enabled);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_Remove_AsksForAConfirmationAndLeavesTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        Assert.True(item.CanRemove);

        var (confirming, cancelled, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, item.BeginRemoveCommand));
            var confirming = item.IsConfirmingRemove;
            page.UpdateLayout();
            ClickCenter(window, Shown(row, harness.Localization.LibraryCancel));
            return Task.FromResult((confirming, item.IsConfirmingRemove, item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        Assert.True(confirming);
        Assert.False(cancelled);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_ConfirmTheRemoval_RemovesTheModAndLeavesTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        var instanceId = viewModel.SelectedInstance!.InstanceId;

        var (opened, selected) = await OnPageAsync(harness, () => new InstancePage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, item.BeginRemoveCommand));
            page.UpdateLayout();
            ClickCenter(window, Shown(row, harness.Localization.ContentRemove));
            if (item.ConfirmRemoveCommand.ExecutionTask is { } running)
                await running;
            return (item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        // the mod really left the instance, which is the proof that the click reached the danger button
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instanceId))!.Mods);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_TheChoices_TakeTheAnswerAndLeaveTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        var choices = OpenChoices(viewModel.SelectedInstance!.InstanceId);
        item.Choices = choices;

        var (opened, selected) = await OnPageAsync(harness, () => new InstancePage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, row.GetVisualDescendants().OfType<CheckBox>().Single(box => box.IsEffectivelyVisible));
            page.UpdateLayout();
            ClickCenter(window, row.GetVisualDescendants().OfType<RadioButton>().First());
            return Task.FromResult((item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        // both answers landed, which is the proof that neither click reached the row
        Assert.True(choices.Recommended.Single().IsSelected);
        Assert.Equal("KSArmory", choices.Alternatives.Single().Selected!.ModId);
        Assert.True(choices.IsComplete);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_ThePrimaryButtonWithAChoiceOpen_IsOffAndDoesNotOpenTheModPage()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        item.Choices = OpenChoices(viewModel.SelectedInstance!.InstanceId);

        var (wasOff, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            var confirm = Shown(row, item.ConfirmUpdateText);
            var wasOff = !confirm.IsEffectivelyEnabled;
            ClickCenter(window, confirm);
            return Task.FromResult((wasOff, item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        Assert.True(wasOff);
        Assert.Null(item.ConfirmUpdateCommand.ExecutionTask);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_ARemovalAnotherModBlocks_IsOffAndDoesNotOpenTheModPage()
    {
        using var harness = await InstanceWithABlockedRemoveAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.SelectMany(group => group.Items).Single(row => row.ModId == ModId);
        Assert.True(item.CanOpen);
        Assert.False(item.CanRemove);

        var (wasOff, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            var trash = Inside(row, item.BeginRemoveCommand);
            var wasOff = !trash.IsEffectivelyEnabled;
            ClickCenter(window, trash);
            return Task.FromResult((wasOff, item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        // the tooltip of the trash explains why it is off, so a click on it must answer nothing at all
        Assert.True(wasOff);
        Assert.False(item.IsConfirmingRemove);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_AChangelogLink_ReachesTheLinkAndLeavesTheModPageClosed()
    {
        using var harness = await InstanceAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();

        // an empty address is refused before anything starts, so the toast is the proof that the command ran
        var link = new ContentLink("Changelog", string.Empty);
        item.Changelogs = [new ReleaseChangelog($"{item.Name} 0.7.5", null, link)];
        Assert.True(item.IsConfirmingUpdate);

        var (opened, selected) = await OnPageAsync(harness, () => new InstancePage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            var linkButton = row.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("link"));
            ClickCenter(window, linkButton);
            return Task.FromResult((item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        Assert.NotEmpty(viewModel.Toasts.Items);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ContentRow_WithoutAModPage_IsNoButtonAndKeepsItsSwitch()
    {
        using var harness = await InstanceWithAnUnlistedModAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.ContentGroups.Single().Items.Single();
        Assert.False(item.CanOpen);

        var (rowButtons, nameInARowButton, opened, selected) = await OnPageAsync(harness, () => new InstancePage(), async (window, page) =>
        {
            var name = page.GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible && text.Text == item.Name);
            var rowButtons = page.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("card-button") && button.IsEffectivelyVisible);
            var inside = name.GetVisualAncestors().OfType<Button>().Any(button => button.Classes.Contains("card-button"));

            // the padding that opens the page on a row which links, and which here must open nothing
            var body = name.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("list-row"));
            Click(window, body, new Point(4, body.Bounds.Height / 2));
            ClickCenter(window, page.GetVisualDescendants().OfType<ToggleSwitch>().Single());
            if (item.ToggleEnabledCommand.ExecutionTask is { } running)
                await running;
            return (rowButtons, inside, item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        Assert.Equal(0, rowButtons);
        Assert.False(nameInARowButton);
        Assert.Null(opened);
        Assert.Null(selected);

        // the row is no button, and its own controls still work
        Assert.False(item.IsEnabled);
    }

    [Fact]
    public async Task DiscoverRow_ThePaddingBesideTheControls_OpensTheModPage()
    {
        using var harness = await DiscoverAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ModId);

        var (running, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickThePadding(window, row);
            var running = item.OpenCommand.ExecutionTask;
            if (running is not null)
                await running;
            return (running, viewModel.CurrentWindowContent, viewModel.SelectedContent?.ModId);
        });

        Assert.NotNull(running);
        Assert.True(opened);
        Assert.Equal(ModId, selected);
    }

    [Fact]
    public async Task DiscoverRow_Add_PlansTheInstallAndLeavesTheModPageClosed()
    {
        using var harness = await DiscoverAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ModId);
        Assert.True(item.CanInstall);

        var (confirming, cancelled, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, item.InstallCommand));
            if (item.InstallCommand.ExecutionTask is { } running)
                await running;

            var confirming = item.IsConfirmingInstall;
            page.UpdateLayout();
            ClickCenter(window, Shown(row, harness.Localization.LibraryCancel));
            return (confirming, item.IsConfirmingInstall, item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        // without a game the compatibility is unknown, so Add asks for a confirmation the Cancel takes back
        Assert.True(confirming);
        Assert.False(cancelled);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task DiscoverRow_ConfirmTheInstall_RunsItAndLeavesTheModPageClosed()
    {
        using var harness = await DiscoverAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ModId);

        var (confirmed, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, item.InstallCommand));
            if (item.InstallCommand.ExecutionTask is { } planning)
                await planning;

            Assert.True(item.IsConfirmingInstall);
            page.UpdateLayout();
            ClickCenter(window, Shown(row, item.ConfirmInstallText));
            if (item.ConfirmInstallCommand.ExecutionTask is { } confirming)
                await confirming;

            return (item.ConfirmInstallCommand.ExecutionTask, item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        // the row left the confirmation and ran the install, so the click reached the primary button
        Assert.NotNull(confirmed);
        Assert.False(item.IsConfirmingInstall);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task DiscoverRow_TheRowMenu_ReachesRemoveAndLeavesTheModPageClosed()
    {
        using var harness = await DiscoverAsync(installed: ModId);
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ModId);
        Assert.True(item.IsInstalled);

        var (flyoutOpen, confirming, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            var menu = row.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Flyout is not null);
            ClickCenter(window, menu);
            var flyoutOpen = menu.Flyout!.IsOpen;

            // the menu lives in a layer of its own above the page, so its item is looked up from the window
            window.UpdateLayout();
            var remove = window.GetVisualDescendants().OfType<MenuItem>()
                .Single(entry => ReferenceEquals(entry.Command, item.BeginRemoveCommand));
            ClickCenter(window, remove);
            return Task.FromResult((flyoutOpen, item.IsConfirmingRemove, item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        Assert.True(flyoutOpen);
        Assert.True(confirming);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task DiscoverRow_ConfirmTheRemoval_RemovesTheModAndLeavesTheModPageClosed()
    {
        using var harness = await DiscoverAsync(installed: ModId);
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ModId);
        var instanceId = viewModel.ActiveInstance!.InstanceId;
        item.BeginRemoveCommand.Execute(null);

        var (opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), async (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Shown(row, harness.Localization.ContentRemove));
            if (item.ConfirmRemoveCommand.ExecutionTask is { } running)
                await running;
            return (item.OpenCommand.ExecutionTask, viewModel.SelectedContent);
        });

        // the mod really left the instance, which is the proof that the click reached the danger button
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instanceId))!.Mods);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task DiscoverRow_TheChoices_TakeTheAnswerAndLeaveTheModPageClosed()
    {
        using var harness = await DiscoverAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ModId);
        var choices = OpenChoices(viewModel.ActiveInstance!.InstanceId);
        item.Choices = choices;

        var (wasOff, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            var confirm = Shown(row, item.ConfirmInstallText);
            var wasOff = !confirm.IsEffectivelyEnabled;
            ClickCenter(window, confirm);
            ClickCenter(window, row.GetVisualDescendants().OfType<CheckBox>().Single());
            page.UpdateLayout();
            ClickCenter(window, row.GetVisualDescendants().OfType<RadioButton>().First());
            return Task.FromResult((wasOff, item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        // the primary button is off until the group has an answer, and neither it nor the answers reached the row
        Assert.True(wasOff);
        Assert.Null(item.ConfirmInstallCommand.ExecutionTask);
        Assert.True(choices.Recommended.Single().IsSelected);
        Assert.Equal("KSArmory", choices.Alternatives.Single().Selected!.ModId);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task DiscoverRow_PauseAndStop_ReachTheRunAndLeaveTheModPageClosed()
    {
        using var harness = await DiscoverAsync();
        var viewModel = harness.ViewModel;
        var item = viewModel.DiscoverItems.Single(row => row.ModId == ModId);
        var run = NewRun(harness);
        run.IsPaused = true;
        item.Run = run;
        item.IsInstalling = true;

        var (resumed, stopped, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), (window, page) =>
        {
            var row = Row(page, item.OpenCommand);
            ClickCenter(window, Inside(row, run.TogglePauseCommand));
            var resumed = !run.IsPaused;
            page.UpdateLayout();
            ClickCenter(window, Inside(row, run.StopCommand));
            return Task.FromResult((resumed, run.IsStopping, item.OpenCommand.ExecutionTask, viewModel.SelectedContent));
        });

        Assert.True(resumed);
        Assert.True(stopped);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task PackRow_ThePaddingBesideTheControls_OpensThePackPage()
    {
        using var harness = await PacksAsync();
        var viewModel = harness.ViewModel;
        var pack = viewModel.DiscoverPacks.Single();

        var (running, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), async (window, page) =>
        {
            var row = Row(page, pack.OpenCommand);
            ClickThePadding(window, row);
            var running = pack.OpenCommand.ExecutionTask;
            if (running is not null)
                await running;
            return (running, viewModel.CurrentWindowPack, viewModel.SelectedPack?.PackId);
        });

        Assert.NotNull(running);
        Assert.True(opened);
        Assert.Equal("tools-pack", selected);
    }

    [Fact]
    public async Task PackRow_NewInstance_AsksForANameAndLeavesThePackPageClosed()
    {
        using var harness = await PacksAsync();
        var viewModel = harness.ViewModel;
        var pack = viewModel.DiscoverPacks.Single();

        var (naming, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), (window, page) =>
        {
            var row = Row(page, pack.OpenCommand);
            ClickCenter(window, Inside(row, pack.NewInstanceCommand));
            return Task.FromResult((viewModel.IsCreatingInstance, pack.OpenCommand.ExecutionTask, viewModel.SelectedPack));
        });

        // the name modal opened with the pack name in it, which is the proof that the click reached the button
        Assert.True(naming);
        Assert.Equal(pack.Name, viewModel.ModalInstanceName);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task PackRow_Add_PlansTheInstallAndLeavesThePackPageClosed()
    {
        using var harness = await PacksAsync();
        var viewModel = harness.ViewModel;
        var pack = viewModel.DiscoverPacks.Single();
        Assert.True(pack.CanInstall);

        var (confirming, cancelled, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), async (window, page) =>
        {
            var row = Row(page, pack.OpenCommand);
            ClickCenter(window, Inside(row, pack.InstallCommand));
            if (pack.InstallCommand.ExecutionTask is { } running)
                await running;

            var confirming = pack.IsConfirmingInstall;
            page.UpdateLayout();
            ClickCenter(window, Shown(row, harness.Localization.LibraryCancel));
            return (confirming, pack.IsConfirmingInstall, pack.OpenCommand.ExecutionTask, viewModel.SelectedPack);
        });

        Assert.True(confirming);
        Assert.False(cancelled);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task PackRow_ConfirmTheInstall_RunsItAndLeavesThePackPageClosed()
    {
        using var harness = await PacksAsync();
        var viewModel = harness.ViewModel;
        var pack = viewModel.DiscoverPacks.Single();

        var (confirmed, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), async (window, page) =>
        {
            var row = Row(page, pack.OpenCommand);
            ClickCenter(window, Inside(row, pack.InstallCommand));
            if (pack.InstallCommand.ExecutionTask is { } planning)
                await planning;

            Assert.True(pack.IsConfirmingInstall);
            page.UpdateLayout();
            ClickCenter(window, Shown(row, pack.ConfirmInstallText));
            if (pack.ConfirmInstallCommand.ExecutionTask is { } confirming)
                await confirming;

            return (pack.ConfirmInstallCommand.ExecutionTask, pack.OpenCommand.ExecutionTask, viewModel.SelectedPack);
        });

        Assert.NotNull(confirmed);
        Assert.False(pack.IsConfirmingInstall);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task PackRow_TheChoices_TakeTheAnswerAndLeaveThePackPageClosed()
    {
        using var harness = await PacksAsync();
        var viewModel = harness.ViewModel;
        var pack = viewModel.DiscoverPacks.Single();
        var choices = OpenChoices(viewModel.ActiveInstance!.InstanceId);
        pack.Choices = choices;

        var (wasOff, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), (window, page) =>
        {
            var row = Row(page, pack.OpenCommand);
            var confirm = Shown(row, pack.ConfirmInstallText);
            var wasOff = !confirm.IsEffectivelyEnabled;
            ClickCenter(window, confirm);
            ClickCenter(window, row.GetVisualDescendants().OfType<CheckBox>().Single());
            page.UpdateLayout();
            ClickCenter(window, row.GetVisualDescendants().OfType<RadioButton>().First());
            return Task.FromResult((wasOff, pack.OpenCommand.ExecutionTask, viewModel.SelectedPack));
        });

        Assert.True(wasOff);
        Assert.Null(pack.ConfirmInstallCommand.ExecutionTask);
        Assert.True(choices.Recommended.Single().IsSelected);
        Assert.Equal("KSArmory", choices.Alternatives.Single().Selected!.ModId);
        Assert.Null(opened);
        Assert.Null(selected);
    }

    [Fact]
    public async Task PackRow_PauseAndStop_ReachTheRunAndLeaveThePackPageClosed()
    {
        using var harness = await PacksAsync();
        var viewModel = harness.ViewModel;
        var pack = viewModel.DiscoverPacks.Single();
        var run = NewRun(harness);
        run.IsPaused = true;
        pack.Run = run;
        pack.IsInstalling = true;

        var (resumed, stopped, opened, selected) = await OnPageAsync(harness, () => new DiscoverPage(), (window, page) =>
        {
            var row = Row(page, pack.OpenCommand);
            ClickCenter(window, Inside(row, run.TogglePauseCommand));
            var resumed = !run.IsPaused;
            page.UpdateLayout();
            ClickCenter(window, Inside(row, run.StopCommand));
            return Task.FromResult((resumed, run.IsStopping, pack.OpenCommand.ExecutionTask, viewModel.SelectedPack));
        });

        Assert.True(resumed);
        Assert.True(stopped);
        Assert.Null(opened);
        Assert.Null(selected);
    }
}
