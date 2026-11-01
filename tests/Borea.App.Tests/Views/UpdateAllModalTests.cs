using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;
using static Borea.App.Tests.ViewModels.UpdateAllChangesTests;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class UpdateAllModalTests
{
    private static async Task<ViewModelHarness> HoldingAsync(string changelog = "- Fixes the HUD.", PlanGate? gate = null, params ModVersionMetadata[] more)
    {
        var harness = await CreateWithGameAsync(gate);
        harness.SpaceDock.Releases.AddRange([Release("alpha", "1.0.0"), Release("alpha", "1.1.0", changelog: changelog), .. more]);
        await InstalledContent.AddAsync(harness, "alpha", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenContentUpdatesCheckedAsync();
        return harness;
    }

    /// <summary>Shows the main window at the size and runs <paramref name="body"/> while it is open.</summary>
    private static Task<T> InWindowAsync<T>(ViewModelHarness harness, double width, double height, Func<MainWindow, UpdateAllModal, Task<T>> body) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var window = new MainWindow { DataContext = harness.ViewModel, Width = width, Height = height };
            window.Show();
            try
            {
                await SettleAsync(window);
                return await body(window, window.GetVisualDescendants().OfType<UpdateAllModal>().Single());
            }
            finally
            {
                // without its view model the window closes at once, so a close does not start the wait for tasks
                window.DataContext = null;
                window.Close();
            }
        });

    private static async Task SettleAsync(Window window)
    {
        window.UpdateLayout();
        await HeadlessApp.FramesAsync(6);
        window.UpdateLayout();
    }

    private static Button VisibleButton(Visual root, string label)
        => root.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && (button.Content as string == label || button.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == label)));

    private static ScrollViewer ScrollerOf(UpdateAllModal modal)
        => modal.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Name == "Scroller");

    private static double TopOf(Visual inner, Visual root) => inner.TranslatePoint(default, root)!.Value.Y;

    private static double BottomOf(Visual inner, Visual root) => inner.TranslatePoint(new Point(0, inner.Bounds.Height), root)!.Value.Y;

    private static Point CenterOf(Visual inner, Visual root) => inner.TranslatePoint(new Point(inner.Bounds.Width / 2, inner.Bounds.Height / 2), root)!.Value;

    private static List<string> VisibleTexts(Visual root)
        => root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Text is not null).Select(text => text.Text!).ToList();

    private static void Press(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static InstallPlan WithReleases(InstallPlan plan, int count) => new(
        plan.InstanceId,
        plan.InstanceState,
        plan.Selections,
        Enumerable.Range(0, count).Select(number => new PlannedInstall(Release($"a-mod-with-a-very-long-id-so-the-row-must-trim-its-name-{number:00}", "1.2.1-beta.13.20261101"), InstallReason.Manual, GameCompatibility.Compatible, null)).ToList(),
        plan.Warnings,
        plan.UnresolvedChoices,
        plan.Conflicts,
        plan.Choices);

    [Fact]
    public async Task Modal_IsOpenExactlyWhileThePlanWaitsOnTheInstancePage()
    {
        using var harness = await HoldingAsync();
        var viewModel = harness.ViewModel;
        var updateAll = viewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            var idle = modal.IsVisible;
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var waiting = modal.IsVisible;
            viewModel.CurrentWindowInstance = false;
            var elsewhere = modal.IsVisible;
            viewModel.CurrentWindowInstance = true;
            var back = modal.IsVisible;
            updateAll.CancelUpdateCommand.Execute(null);
            return (idle, waiting, elsewhere, back, Cancelled: modal.IsVisible);
        });

        Assert.Equal((false, true, false, true, false), seen);
    }

    [Fact]
    public async Task TheModal_StandsAfterTheModListModal_AndBelowTheSettingsAndTheCloseQuestion()
    {
        using var harness = await HoldingAsync();

        var order = await InWindowAsync(harness, 1000, 832, (_, modal) =>
        {
            var hosts = ((Panel)modal.Parent!).Children;
            int At<T>() => hosts.IndexOf(hosts.OfType<T>().Cast<Control>().Single());
            return Task.FromResult((Modal: hosts.IndexOf(modal), List: At<ModListModal>(), Settings: At<Borea.App.Views.Settings.SettingsModal>(), Close: At<CloseNowModal>()));
        });

        Assert.Equal(order.List + 1, order.Modal);
        Assert.True(order.Modal < order.Settings, "The settings must draw over the modal.");
        Assert.True(order.Modal < order.Close, "The close question must draw over the modal.");
    }

    [Fact]
    public async Task WhileChecking_ThePageHidesPauseAndTheBarHasNoPercent_UntilTheFirstReport()
    {
        var gate = new PlanGate();
        using var harness = await HoldingAsync(gate: gate);
        var updateAll = harness.ViewModel.UpdateAll!;
        var localization = harness.Localization;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, _) =>
        {
            gate.Close();
            var update = updateAll.UpdateCommand.ExecuteAsync(null);
            await gate.Reached;
            await SettleAsync(window);
            var bar = window.GetVisualDescendants().OfType<Borea.App.Views.Pages.InstancePage>().Single().GetVisualDescendants().OfType<ProgressBar>().Single(item => item.IsEffectivelyVisible);
            var checking = (bar.IsIndeterminate, VisibleTexts(window).Contains(localization.InstallPause), VisibleTexts(window).Contains(localization.InstallStop), VisibleTexts(window).Contains(localization.UpdateChecking));

            updateAll.Run!.Report(InstallPhase.Downloading);
            await SettleAsync(window);
            var reported = (bar.IsIndeterminate, VisibleTexts(window).Contains(localization.InstallPause));

            gate.Open();
            await update;
            return (checking, reported);
        });

        Assert.Equal((true, false, true, true), seen.checking);
        Assert.Equal((false, true), seen.reported);
    }

    [Fact]
    public async Task ThirtyModsInTheSmallestWindow_TheMiddleScrollsAndTheButtonsStay()
    {
        using var harness = await HoldingAsync();
        var updateAll = harness.ViewModel.UpdateAll!;
        var localization = harness.Localization;

        var seen = await InWindowAsync(harness, 860, 480, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            updateAll.PendingPlan = WithReleases(updateAll.PendingPlan!, 30);
            await SettleAsync(window);
            var scroller = modal.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Name == "Scroller");
            return (
                ConfirmBottom: BottomOf(VisibleButton(modal, updateAll.ConfirmUpdateText), window),
                CancelBottom: BottomOf(VisibleButton(modal, localization.LibraryCancel), window),
                Rows: updateAll.Changes.Count,
                scroller.ScrollBarMaximum.Y);
        });

        Assert.Equal(30, seen.Rows);
        Assert.True(seen.ConfirmBottom <= 480, $"The button ends at {seen.ConfirmBottom}.");
        Assert.True(seen.CancelBottom <= 480, $"The button ends at {seen.CancelBottom}.");
        Assert.True(seen.Y > 0, "The list must scroll.");
    }

    [Fact]
    public async Task ALongChangelogThatOpens_ScrollsAndTheButtonsStayEnabledInView()
    {
        var changelog = string.Join("\n", Enumerable.Range(1, 80).Select(line => $"- Change {line} of the release."));
        using var harness = await HoldingAsync(changelog);
        var updateAll = harness.ViewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 860, 480, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var confirm = VisibleButton(modal, updateAll.ConfirmUpdateText);
            var scroller = modal.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Name == "Scroller");
            return (updateAll.Changes[0].IsExpanded, Bottom: BottomOf(confirm, window), confirm.IsEffectivelyEnabled, scroller.ScrollBarMaximum.Y);
        });

        Assert.True(seen.IsExpanded);
        Assert.True(seen.Bottom <= 480, $"The button ends at {seen.Bottom}.");
        Assert.True(seen.IsEffectivelyEnabled);
        Assert.True(seen.Y > 0, "The changelog must scroll.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BackdropAndEscape_CancelThePlan(bool escape)
    {
        using var harness = await HoldingAsync();
        var updateAll = harness.ViewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            if (escape)
            {
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            }
            else
            {
                Press(window, new Point(100, 100));
            }

            await SettleAsync(window);
            return (updateAll.IsConfirmingUpdate, updateAll.PendingPlan);
        });

        Assert.False(seen.IsConfirmingUpdate);
        Assert.Null(seen.PendingPlan);
    }

    [Fact]
    public async Task DuringTheSecondPlan_NothingCancels_AndEveryButtonIsOff()
    {
        var gate = new PlanGate();
        using var harness = await HoldingAsync(gate: gate, more: [Release("alpha", "1.2.0", dependencies: [new ModDependency("library", ModDependencyKind.Recommends)]), Release("library", "1.0.0")]);
        var updateAll = harness.ViewModel.UpdateAll!;
        var localization = harness.Localization;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var confirmText = updateAll.ConfirmUpdateText;
            gate.Close();
            var confirming = updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
            await gate.Reached;
            await SettleAsync(window);

            var cancel = VisibleButton(modal, localization.LibraryCancel);
            var close = modal.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CloseButton");
            var confirm = VisibleButton(modal, confirmText);
            var stop = VisibleButton(modal, localization.InstallStop);
            var offAndBusy = (cancel.IsEffectivelyEnabled, close.IsEffectivelyEnabled, confirm.IsEffectivelyEnabled, Stop: stop.IsEffectivelyEnabled, Busy: VisibleTexts(modal).Contains(localization.UpdateChecking));

            Press(window, new Point(100, 100));
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            var stillOpen = modal.IsVisible;

            gate.Open();
            await confirming;
            await SettleAsync(window);
            return (offAndBusy, stillOpen, Closed: modal.IsVisible);
        });

        Assert.Equal((false, false, false, true, true), seen.offAndBusy);
        Assert.True(seen.stillOpen);
        Assert.False(seen.Closed);
    }

    [Fact]
    public async Task AStopDuringTheSecondPlan_ClosesTheModal_SaysSo_AndInstallsNothing()
    {
        var gate = new PlanGate();
        using var harness = await HoldingAsync(gate: gate, more: [Release("alpha", "1.2.0", dependencies: [new ModDependency("library", ModDependencyKind.Recommends)]), Release("library", "1.0.0")]);
        var updateAll = harness.ViewModel.UpdateAll!;
        var localization = harness.Localization;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            gate.Close();
            var confirming = updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
            await gate.Reached;
            await SettleAsync(window);

            Press(window, CenterOf(VisibleButton(modal, localization.InstallStop), window));

            // the gate stays closed, so only the stop can end this plan
            await confirming.WaitAsync(TimeSpan.FromSeconds(30));
            await SettleAsync(window);
            return (modal.IsVisible, updateAll.ProgressStatus, updateAll.IsInstalling);
        });

        gate.Open();
        Assert.Equal((false, localization.UpdateStopped, false), seen);
        Assert.Equal(["alpha"], (await harness.Services.Instances.GetAllAsync()).Single().Mods.Where(mod => mod.Version == ModVersion.Parse("1.0.0")).Select(mod => mod.ModId));
    }

    [Fact]
    public async Task ANewWarningFromTheSecondPlan_KeepsTheModalWithEnabledButtons_AndShowsTheTop()
    {
        var gate = new PlanGate();
        using var harness = await HoldingAsync(gate: gate, more: [Release("alpha", "1.2.0", dependencies: [new ModDependency("library", ModDependencyKind.Recommends)]), Release("library", "1.0.0")]);
        var updateAll = harness.ViewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 860, 480, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            updateAll.PendingPlan = WithReleases(updateAll.PendingPlan!, 30);
            await SettleAsync(window);
            ScrollerOf(modal).Offset = new Vector(0, 100);
            await SettleAsync(window);
            var scrolled = ScrollerOf(modal).Offset.Y;
            gate.Edit = plan => plan.WithWarnings([new PlanningMessage("alpha", PlanningMessageKind.Compatibility)]);

            await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            return (scrolled, modal.IsVisible, Top: ScrollerOf(modal).Offset.Y, updateAll.ConfirmUpdateText, Enabled: VisibleButton(modal, updateAll.ConfirmUpdateText).IsEffectivelyEnabled, Cancel: VisibleButton(modal, harness.Localization.LibraryCancel).IsEffectivelyEnabled);
        });

        Assert.True(seen.scrolled > 0, "The list must have scrolled first.");
        Assert.True(seen.IsVisible);
        Assert.Equal(0, seen.Top);
        Assert.Contains("anyway", seen.ConfirmUpdateText);
        Assert.True(seen.Enabled);
        Assert.True(seen.Cancel);
    }

    [Fact]
    public async Task OpeningAgain_StartsAtTheTop()
    {
        using var harness = await HoldingAsync();
        var updateAll = harness.ViewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 860, 480, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            updateAll.PendingPlan = WithReleases(updateAll.PendingPlan!, 30);
            await SettleAsync(window);
            ScrollerOf(modal).Offset = new Vector(0, 100);
            await SettleAsync(window);
            var scrolled = ScrollerOf(modal).Offset.Y;

            updateAll.CancelUpdateCommand.Execute(null);
            await SettleAsync(window);
            await updateAll.UpdateCommand.ExecuteAsync(null);
            updateAll.PendingPlan = WithReleases(updateAll.PendingPlan!, 30);
            await SettleAsync(window);
            return (scrolled, Top: ScrollerOf(modal).Offset.Y);
        });

        Assert.True(seen.scrolled > 0, "The list must have scrolled first.");
        Assert.Equal(0, seen.Top);
    }

    [Fact]
    public async Task ARequiredChoice_StandsInViewAboveTheList_UnderAHeadingThatAsksForIt()
    {
        using var harness = await HoldingAsync(more:
        [
            Release("alpha", "1.2.0", dependencies: [ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("first"), new ModDependencyAlternative("second")])]),
            Release("first", "1.0.0"),
            Release("second", "1.0.0"),
        ]);
        var updateAll = harness.ViewModel.UpdateAll!;
        var localization = harness.Localization;

        var seen = await InWindowAsync(harness, 860, 480, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var scroller = ScrollerOf(modal);
            var lastOption = modal.GetVisualDescendants().OfType<RadioButton>().Last(button => button.IsEffectivelyVisible);
            var heading = modal.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == localization.UpdateAllChooseMod);
            var list = modal.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == updateAll.ListHeadingText);
            return (OptionBottom: BottomOf(lastOption, window), ScrollerBottom: BottomOf(scroller, window), HeadingTop: TopOf(heading, window), ListTop: TopOf(list, window), Texts: VisibleTexts(modal));
        });

        Assert.True(seen.OptionBottom <= seen.ScrollerBottom, $"The last option ends at {seen.OptionBottom}, and the view at {seen.ScrollerBottom}.");
        Assert.True(seen.HeadingTop < seen.ListTop, "The choice must stand above the list.");
        Assert.DoesNotContain(localization.UpdateAllMoreMods, seen.Texts);
        Assert.Contains(localization.UpdateAllChooseFirst, seen.Texts);
    }

    [Fact]
    public async Task OptionalChoices_StandBelowTheList_UnderMoreMods()
    {
        using var harness = await HoldingAsync(more:
        [
            Release("alpha", "1.2.0", dependencies: [new ModDependency("library", ModDependencyKind.Recommends)]),
            Release("library", "1.0.0"),
        ]);
        var updateAll = harness.ViewModel.UpdateAll!;
        var localization = harness.Localization;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var heading = modal.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == localization.UpdateAllMoreMods);
            var list = modal.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == updateAll.ListHeadingText);
            return (HeadingTop: TopOf(heading, window), ListTop: TopOf(list, window));
        });

        Assert.True(seen.ListTop < seen.HeadingTop, "The optional choices must stand below the list.");
    }

    [Fact]
    public async Task ALongVersionKeepsItsWidth_AndALongNameTakesTwoLines()
    {
        const string LongName = "The Extremely Long Named Orbital Mechanics Overhaul Collection With Many Parts";
        using var harness = await HoldingAsync();
        var updateAll = harness.ViewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 860, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            updateAll.Changes = [new UpdateChangeItem(harness.Localization, "long", LongName, ModVersion.Parse("1.2.0-beta.12.20261029"), ModVersion.Parse("1.2.1-beta.13.20261101"), [])];
            await SettleAsync(window);
            var texts = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToList();
            var name = texts.Single(text => text.Text == LongName);
            return (Name: name.Bounds.Height, Line: name.LineHeight, From: texts.Single(text => text.Text == "1.2.0-beta.12.20261029").Bounds.Width, To: texts.Single(text => text.Text == "1.2.1-beta.13.20261101").Bounds.Width, NoNotes: texts.Any(text => text.Text == harness.Localization.UpdateAllNoNotes));
        });

        Assert.True(seen.From > 120, $"The installed version is {seen.From} wide.");
        Assert.True(seen.To > 120, $"The new version is {seen.To} wide.");
        Assert.True(seen.Name > 24, $"The name is {seen.Name} high, and must take two lines.");
        Assert.True(seen.NoNotes);
    }

    [Fact]
    public async Task ASaveThatBreaks_HasItsMark_AndAnotherWarningHasNone()
    {
        using var harness = await HoldingAsync();
        var updateAll = harness.ViewModel.UpdateAll!;

        var marks = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            int Marks() => modal.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Count(path => path.Name == "BreakingMark" && path.IsEffectivelyVisible);
            await updateAll.UpdateCommand.ExecuteAsync(null);
            var plan = updateAll.PendingPlan!;
            var compatibility = new PlanningMessage("alpha", PlanningMessageKind.Compatibility);
            updateAll.PendingPlan = plan.WithWarnings([compatibility]);
            await SettleAsync(window);
            var plain = Marks();
            updateAll.PendingPlan = plan.WithWarnings([compatibility, new PlanningMessage("beta", PlanningMessageKind.BreaksSave) { Dependency = new ModDependency("beta", ModDependencyKind.Required), Value = "Mun Landing" }]);
            await SettleAsync(window);
            return (plain, Breaking: Marks());
        });

        Assert.Equal((0, 1), marks);
    }

    [Fact]
    public async Task TheBusyLibraryLine_StandsInTheFooter_AndLeavesAfterAPressThatWorks()
    {
        using var harness = await HoldingAsync();
        var viewModel = harness.ViewModel;
        var updateAll = viewModel.UpdateAll!;
        var localization = harness.Localization;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            viewModel.IsChangingLibraryFolder = true;
            await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var busy = VisibleTexts(modal).Contains(localization.LibraryFolderBusy);

            viewModel.IsChangingLibraryFolder = false;
            await updateAll.ConfirmUpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            return (busy, Open: modal.IsVisible, Later: VisibleTexts(window).Contains(localization.LibraryFolderBusy));
        });

        Assert.Equal((true, false, false), seen);
    }

    [Fact]
    public async Task TheFocusMovesIntoTheList_AndEnterNeitherPlansAgainNorStarts()
    {
        using var harness = await HoldingAsync();
        var viewModel = harness.ViewModel;
        var updateAll = viewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            var link = VisibleButton(window, harness.Localization.InstanceUpdateAll);
            link.Focus(NavigationMethod.Tab);
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var plan = updateAll.PendingPlan;
            var focused = window.FocusManager!.GetFocusedElement() as Control;

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await SettleAsync(window);
            return (focused?.Name, Same: ReferenceEquals(plan, updateAll.PendingPlan), updateAll.IsConfirmingUpdate);
        });

        Assert.Equal(("Scroller", true, true), seen);
    }

    [Fact]
    public async Task APressOnARowFoldsItsChangelog_AndAPressInTheChangelogDoesNot()
    {
        using var harness = await HoldingAsync(more: [Release("beta", "1.0.0"), Release("beta", "1.1.0", changelog: "- Fixes the menu.")]);
        await InstalledContent.AddAsync(harness, "beta", activate: true, ownership: ModInstallOwnership.Borea, version: "1.0.0");
        await harness.ViewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var updateAll = harness.ViewModel.UpdateAll!;

        var seen = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            var change = updateAll.Changes.Single(item => item.Name == "beta");
            var folded = modal.GetVisualDescendants().OfType<MarkdownView>().Count();
            var name = modal.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == "beta");

            Press(window, CenterOf(name, window));
            await SettleAsync(window);
            var opened = change.IsExpanded;
            var view = modal.GetVisualDescendants().OfType<MarkdownView>().Single();

            Press(window, CenterOf(view, window));
            await SettleAsync(window);
            return (folded, opened, InTheChangelog: change.IsExpanded);
        });

        Assert.Equal((0, true, true), seen);
    }

    [Fact]
    public async Task TheTabsStayWhereTheyAre_WhileThePlanWaits()
    {
        using var harness = await HoldingAsync();
        var updateAll = harness.ViewModel.UpdateAll!;

        var tops = await InWindowAsync(harness, 1000, 832, async (window, _) =>
        {
            var tab = VisibleButton(window, harness.Localization.InstanceTabContent);
            var before = tab.TranslatePoint(default, window)!.Value.Y;
            await updateAll.UpdateCommand.ExecuteAsync(null);
            await SettleAsync(window);
            return (before, After: tab.TranslatePoint(default, window)!.Value.Y);
        });

        Assert.Equal(tops.before, tops.After);
    }

    [Fact]
    public async Task ChoicesInTheModal_ExplainTheTick_AndTheBlockedLineShowsOnceInTheFooter()
    {
        using var harness = await HoldingAsync(more:
        [
            Release("alpha", "1.2.0", dependencies: [new ModDependency("library", ModDependencyKind.Recommends), new ModDependency("extra", ModDependencyKind.Suggests)]),
            Release("library", "1.0.0"),
            Release("extra", "1.0.0"),
        ]);
        var updateAll = harness.ViewModel.UpdateAll!;
        var localization = harness.Localization;

        var (texts, buttonTop, blockedTop) = await InWindowAsync(harness, 1000, 832, async (window, modal) =>
        {
            await updateAll.UpdateCommand.ExecuteAsync(null);
            updateAll.Choices!.BlockedText = "The plan cannot run.";
            await SettleAsync(window);
            var blocked = modal.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == "The plan cannot run.");
            var cancel = VisibleButton(modal, localization.LibraryCancel);
            return (VisibleTexts(modal), cancel.TranslatePoint(default, window)!.Value.Y, blocked.TranslatePoint(default, window)!.Value.Y);
        });

        Assert.Contains(localization.InstallChoicesRecommendedHint, texts);
        Assert.Contains(localization.InstallChoicesSuggestedHint, texts);
        Assert.Single(texts, text => text == "The plan cannot run.");
        Assert.True(blockedTop < buttonTop, "The line must stand directly above the buttons.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstallChoices_ShowsTheBlockedLineUnlessItsHostKeepsIt(bool shows)
    {
        const string Blocked = "The plan cannot run.";

        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var choices = new Borea.App.ViewModels.InstallChoices(Guid.NewGuid(), [], modId => modId) { BlockedText = Blocked };
            var view = new Borea.App.Views.InstallChoices { DataContext = choices };
            if (!shows)
                view.ShowsBlockedText = false;

            var window = new Window { Content = view, Width = 400, Height = 300 };
            window.Show();
            try
            {
                await SettleAsync(window);
                return VisibleTexts(window).Contains(Blocked);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(shows, seen);
    }
}
