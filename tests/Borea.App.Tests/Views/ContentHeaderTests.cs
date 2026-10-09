using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Core.History;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

/// <summary>
/// The top of the content and pack pages: the banner that names where the page
/// was opened from, the place of the Installed chip, and the actions beside or
/// below the title.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ContentHeaderTests
{
    private const string ModId = "AdvancedFlightComputer";

    private static Task<T> OnContentPageAsync<T>(ViewModelHarness harness, double width, Func<Window, Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new Borea.App.Views.Pages.ContentPage();
            var window = new Window { Width = width, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return Task.FromResult(read(window, page));
            }
            finally
            {
                window.Close();
            }
        });

    private static Rect BoundsIn(Visual page, Visual control)
        => new(control.TranslatePoint(default, page)!.Value, control.Bounds.Size);

    private static TextBlock? Shown(Control page, string text)
        => page.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(block => block.IsEffectivelyVisible && block.Text == text);

    private static Border InstalledChip(Control page, string installed)
        => page.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("chip") && border.IsEffectivelyVisible && border.Child is TextBlock { Text: var text } && text == installed);

    private static Button RemoveButton(Control page)
        => page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Classes.Contains("danger"));

    private static async Task<ViewModelHarness> InstalledAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        await InstalledContent.AddAsync(harness, ModId, activate: true, ownership: ModInstallOwnership.Borea);
        await harness.ViewModel.LoadAsync();
        return harness;
    }

    /// <summary>An active instance that holds another mod, so the content page offers Add.</summary>
    private static async Task<ViewModelHarness> WithActiveInstanceAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await harness.ViewModel.LoadAsync();
        return harness;
    }

    private static Task<ViewModelHarness> FlightPlanningEssentialsAsync() =>
        ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "flight-planning-essentials",
            "Flight Planning Essentials",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin(ModId, "0.7.4"), PackViewModelTests.Pin("KSArmory", "0.8.44"), PackViewModelTests.Pin("MeasureTools", "1.1.9")))));

    public enum HeaderState
    {
        Add,
        Installing,
        Stopping,
        Installed,
    }

    /// <summary>Shows the buttons of <paramref name="state"/> in the header of the open pack or content page.</summary>
    private static void SetState(ViewModelHarness harness, bool pack, HeaderState state)
    {
        var viewModel = harness.ViewModel;
        InstallRun? run = null;
        if (state is HeaderState.Installing or HeaderState.Stopping)
        {
            var task = new TaskRegistry(harness.Localization, () => null, () => null, _ => Task.CompletedTask).Start(TaskKind.ModInstall, null, null, null, null, null, TaskState.Running);
            run = new InstallRun(harness.Localization, task) { IsFinishingMod = state == HeaderState.Stopping, IsStopping = state == HeaderState.Stopping };
        }

        if (pack)
        {
            viewModel.SelectedPack!.Run = run;
            viewModel.SelectedPack.IsInstalling = run is not null;
            viewModel.SelectedPack.IsInstalled = state == HeaderState.Installed;
        }
        else
        {
            viewModel.SelectedContent!.Run = run;
            viewModel.SelectedContent.IsInstalling = run is not null;
            viewModel.SelectedContent.IsInstalled = state == HeaderState.Installed;
        }
    }

    private static async Task OpenAsync(ViewModelHarness harness, bool pack)
    {
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        if (pack)
        {
            viewModel.ShowDiscoverModpacksCommand.Execute(null);
            await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);
        }
        else
        {
            await viewModel.DiscoverItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Renders the pack or the content page next to a navigation rail, as the main window does.</summary>
    private static Task<T> OnPageWithRailAsync<T>(ViewModelHarness harness, bool pack, double width, Func<Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            Control page = pack ? new Borea.App.Views.Pages.PackPage() : new Borea.App.Views.Pages.ContentPage();
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = width, Height = 900, DataContext = harness.ViewModel, Content = body };
            window.Show();
            try
            {
                window.UpdateLayout();
                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>The width of <paramref name="value"/> on one line in the font of <paramref name="text"/>.</summary>
    private static double NaturalWidth(TextBlock text, string? value)
        => new TextLayout(value ?? string.Empty, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight), text.FontSize, null).Width;

    /// <summary>The words of <paramref name="text"/> that it breaks inside, or that are wider than the text block.</summary>
    private static IEnumerable<string> BrokenWords(TextBlock text)
    {
        var value = text.Text ?? string.Empty;
        foreach (var word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var wordWidth = NaturalWidth(text, word);
            if (wordWidth > text.Bounds.Width + 0.5)
                yield return $"{word} is {wordWidth} wide, but {value} has {text.Bounds.Width}";
        }

        foreach (var line in text.TextLayout.TextLines.Skip(1))
        {
            var start = line.FirstTextSourceIndex;
            if (start > 0 && start < value.Length && !char.IsWhiteSpace(value[start - 1]))
                yield return $"{value} breaks inside a word at {start}";
        }
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task OpenedFromAnInstance_TheInstalledChipLeadsTheMetaRowAndRemoveStaysInTheActions(double width)
    {
        using var harness = await InstalledAsync();
        var viewModel = harness.ViewModel;
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.ContentGroups.SelectMany(group => group.Items).Single().OpenCommand.ExecuteAsync(null);

        var (chip, remove, name) = await OnContentPageAsync(harness, width, (_, page) => (
            BoundsIn(page, InstalledChip(page, harness.Localization.DiscoverInstalled)),
            BoundsIn(page, RemoveButton(page)),
            BoundsIn(page, Shown(page, viewModel.SelectedContent!.Name)!)));

        Assert.True(chip.Top >= remove.Bottom, $"The chip at {chip} is not below Remove at {remove}.");
        Assert.Equal(name.Left, chip.Left, 0.5);
    }

    [Fact]
    public async Task OpenedFromDiscover_TheInstalledChipStaysBesideRemove()
    {
        using var harness = await InstalledAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);

        var (chip, remove) = await OnContentPageAsync(harness, 1280, (_, page) => (
            BoundsIn(page, InstalledChip(page, harness.Localization.DiscoverInstalled)),
            BoundsIn(page, RemoveButton(page))));

        Assert.Equal(remove.Center.Y, chip.Center.Y, 0.5);
        Assert.True(chip.Right <= remove.Left);
    }

    [Theory]
    [InlineData(false, 860, ContentHeaderActionsPlace.BelowAll)]
    [InlineData(false, 1280, ContentHeaderActionsPlace.BelowText)]
    [InlineData(false, 1920, ContentHeaderActionsPlace.BesideText)]
    [InlineData(true, 860, ContentHeaderActionsPlace.BelowAll)]
    [InlineData(true, 1280, ContentHeaderActionsPlace.BelowText)]
    [InlineData(true, 1920, ContentHeaderActionsPlace.BesideText)]
    public async Task NarrowPageOnly_PutsTheActionsBelowTheText(bool pack, double width, ContentHeaderActionsPlace place)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: InstanceViewModelTests.WithPack("starter-pack", "Starter Pack", "1.0.0", (ModId, "0.7.5")));
        await OpenAsync(harness, pack);

        var (actual, image, text, actions) = await OnPageWithRailAsync(harness, pack, width, page =>
        {
            var header = page.GetVisualDescendants().OfType<ContentHeaderPanel>().Single();
            return (header.ActionsPlace, BoundsIn(header, header.Children[0]), BoundsIn(header, header.Children[1]), BoundsIn(header, header.Children[2]));
        });

        Assert.Equal(place, actual);
        var where = place switch
        {
            ContentHeaderActionsPlace.BesideText => actions.Left >= text.Right && actions.Top == text.Top,
            ContentHeaderActionsPlace.BelowText => actions.Top >= text.Bottom && actions.Left == text.Left,
            _ => actions.Top >= Math.Max(image.Bottom, text.Bottom) && actions.Left == image.Left,
        };
        Assert.True(where, $"The actions at {actions} are not {place} with the image at {image} and the text at {text}.");
    }

    [Theory]
    [InlineData(true, "en", 860, HeaderState.Add)]
    [InlineData(true, "de", 860, HeaderState.Add)]
    [InlineData(true, "en", 1280, HeaderState.Add)]
    [InlineData(true, "de", 1280, HeaderState.Add)]
    [InlineData(true, "de", 1920, HeaderState.Add)]
    [InlineData(false, "en", 860, HeaderState.Installed)]
    [InlineData(false, "de", 1280, HeaderState.Installed)]
    [InlineData(true, "en", 860, HeaderState.Stopping)]
    [InlineData(true, "de", 860, HeaderState.Stopping)]
    [InlineData(false, "en", 860, HeaderState.Stopping)]
    [InlineData(false, "de", 860, HeaderState.Stopping)]
    public async Task Header_ShowsEveryButtonWholeAndKeepsTheWordsOfTheTitleWhole(bool pack, string culture, double width, HeaderState state)
    {
        using var harness = pack ? await FlightPlanningEssentialsAsync() : await WithActiveInstanceAsync();
        Assert.True(harness.Localization.TrySetCulture(culture));
        await OpenAsync(harness, pack);
        SetState(harness, pack, state);
        var viewModel = harness.ViewModel;
        var title = pack ? viewModel.SelectedPack!.Name : viewModel.SelectedContent!.Name;
        string[] texts = pack ? [title, viewModel.SelectedPack!.NewerReleasesText!] : [title];

        var problems = await OnPageWithRailAsync(harness, pack, width, page =>
        {
            var body = page.GetVisualDescendants().OfType<PageBodyPanel>().Single().GetVisualChildren().OfType<StackPanel>().Single();
            var header = page.GetVisualDescendants().OfType<ContentHeaderPanel>().Single();
            var problems = new List<string>();
            var buttons = header.Children[2].GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
            if (buttons.Count < (pack ? 3 : 2))
                problems.Add($"only {buttons.Count} buttons show in the header");
            foreach (var button in buttons)
            {
                var bounds = BoundsIn(body, button);
                if (bounds.Left < -0.5 || bounds.Right > body.Bounds.Width + 0.5)
                    problems.Add($"the button at {bounds} ends outside the body, which is {body.Bounds.Width} wide");
                foreach (var label in button.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible))
                {
                    var inButton = BoundsIn(button, label);
                    if (inButton.Left < -0.5 || inButton.Right > button.Bounds.Width + 0.5 || NaturalWidth(label, label.Text) > label.Bounds.Width + 0.5)
                        problems.Add($"{label.Text} does not show whole in its button");
                }
            }

            foreach (var text in texts)
                problems.AddRange(BrokenWords(Shown(page, text)!));
            return problems;
        });

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "de")]
    [InlineData(false, "en")]
    [InlineData(false, "de")]
    public async Task Header_KeepsTheActionsInOnePlaceWhileTheButtonsChange(bool pack, string culture)
    {
        using var harness = pack ? await FlightPlanningEssentialsAsync() : await WithActiveInstanceAsync();
        Assert.True(harness.Localization.TrySetCulture(culture));
        await OpenAsync(harness, pack);
        double[] widths = [860, 900, 1000, 1100, 1280, 1460, 1600];

        var places = await HeadlessApp.RunAsync(harness, () =>
        {
            Control page = pack ? new Borea.App.Views.Pages.PackPage() : new Borea.App.Views.Pages.ContentPage();
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = widths[0], Height = 900, DataContext = harness.ViewModel, Content = body };
            window.Show();
            try
            {
                var places = new List<(double Width, HeaderState State, ContentHeaderActionsPlace Place)>();
                foreach (var width in widths)
                {
                    window.Width = width;
                    foreach (var state in Enum.GetValues<HeaderState>())
                    {
                        SetState(harness, pack, state);
                        window.UpdateLayout();
                        places.Add((width, state, page.GetVisualDescendants().OfType<ContentHeaderPanel>().Single().ActionsPlace));
                    }
                }

                return Task.FromResult(places);
            }
            finally
            {
                window.Close();
            }
        });

        var moves = places.GroupBy(place => place.Width).Where(group => group.Select(place => place.Place).Distinct().Count() > 1);
        Assert.Empty(moves.Select(group => string.Join(", ", group)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenedFromHomeOrDiscover_ShowsNoBanner(bool fromHome)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        if (fromHome)
        {
            await viewModel.RecentItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);
        }
        else
        {
            await viewModel.EnsureDiscoverLoadedAsync();
            await viewModel.DiscoverItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);
        }

        var banner = await OnContentPageAsync(harness, 1280, (_, page) => Shown(page, harness.Localization.ContentInsideInstance));

        Assert.Null(banner);
    }
}
