using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class MarkdownSelectionTests
{
    private const string Mixed = "The intro text.\n\n## The heading\n\n- first item\n- second item\n\nThe outro text.";

    private static readonly string[] MixedSelection = ["intro text.", "The heading", "first item", "second item", "The outro"];

    /// <summary>
    /// Shows the text in a window, with a text box below it when <paramref name="withTextBox"/> is set, and runs
    /// <paramref name="probe"/> while the window is open.
    /// </summary>
    private static Task<T> ShowAsync<T>(string markdown, Func<Window, MarkdownView, Task<T>> probe, MainViewModel? viewModel = null, bool withTextBox = false) =>
        HeadlessApp.RunAsync(async () =>
        {
            var view = new MarkdownView { Markdown = markdown };
            var window = new Window { Width = 800, Height = 600, Content = withTextBox ? new StackPanel { Children = { view, new TextBox() } } : view, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                return await probe(window, view);
            }
            finally
            {
                window.Close();
            }
        });

    private static Task<T> RenderAsync<T>(string markdown, Func<Window, MarkdownView, T> probe, MainViewModel? viewModel = null) =>
        ShowAsync(markdown, (window, view) => Task.FromResult(probe(window, view)), viewModel);

    private static List<SelectableTextBlock> Texts(Visual view) => view.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();

    private static string Shown(TextBlock text) => (text.Inlines is { Count: > 0 } inlines ? inlines.Text : text.Text) ?? string.Empty;

    private static SelectableTextBlock Find(Visual view, string text) => Assert.Single(Texts(view), block => Shown(block).StartsWith(text, StringComparison.Ordinal));

    /// <summary>
    /// A point just inside the left edge of the first character of <paramref name="part"/> in the text that starts with
    /// <paramref name="block"/>, so a press there puts the caret before it.
    /// </summary>
    private static Point EdgeAt(Window window, Visual view, string block, string part)
    {
        var text = Find(view, block);
        var character = text.TextLayout.HitTestTextPosition(Shown(text).IndexOf(part, StringComparison.Ordinal));
        return text.TranslatePoint(new Point(text.Padding.Left + character.X + 1, text.Padding.Top + character.Center.Y), window)!.Value;
    }

    /// <summary>A point 8 pixels after the end of the text that starts with <paramref name="block"/>, where a press hits no character of it.</summary>
    private static Point AfterTheEnd(Window window, Visual view, string block)
    {
        var text = Find(view, block);
        var end = text.TextLayout.HitTestTextPosition(Shown(text).Length);
        return text.TranslatePoint(new Point(end.X + 8, end.Center.Y), window)!.Value;
    }

    private static void Drag(Window window, Point from, params Point[] path)
    {
        window.MouseDown(from, MouseButton.Left);
        foreach (var point in path)
            window.MouseMove(point, RawInputModifiers.LeftMouseButton);
        window.MouseUp(path[^1], MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static PlatformHotkeyConfiguration Keys => Application.Current!.PlatformSettings!.HotkeyConfiguration;

    /// <summary>Presses the gesture the platform uses, so the test runs with Ctrl on Windows and Linux and with Cmd on macOS.</summary>
    private static void Press(Window window, KeyGesture gesture)
    {
        window.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> Selected(Visual view) => Texts(view).Select(text => text.SelectedText).Where(selected => selected.Length > 0).ToList();

    private static async Task<string?> ClipboardAsync(Window window) => await window.Clipboard!.TryGetTextAsync();

    [Fact]
    public async Task Drag_FromAParagraphOverAHeadingAndAListIntoTheLastParagraph_SelectsAndCopiesEveryTextBetween()
    {
        var (selected, copied) = await ShowAsync(Mixed, async (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "The intro", "intro"), EdgeAt(window, view, "The outro", " text."));
            var selected = Selected(view);
            Press(window, Keys.Copy[0]);
            return (selected, await ClipboardAsync(window));
        });

        Assert.Equal(MixedSelection, selected);
        Assert.Equal(string.Join("\n\n", MixedSelection), copied);
    }

    [Fact]
    public async Task Copy_OfADragThatStartedAfterTheEndOfAText_CopiesTheTextsAfterIt()
    {
        var (selected, copied) = await ShowAsync(Mixed, async (window, view) =>
        {
            Drag(window, AfterTheEnd(window, view, "The intro"), EdgeAt(window, view, "The outro", " text."));
            var selected = Selected(view);
            Press(window, Keys.Copy[0]);
            return (selected, await ClipboardAsync(window));
        });

        Assert.Equal(MixedSelection[1..], selected);
        Assert.Equal(string.Join("\n\n", MixedSelection[1..]), copied);
    }

    [Fact]
    public async Task Drag_FromAfterTheEndOfATextToBelowTheView_SelectsToTheEndOfTheLastText()
    {
        var selected = await ShowAsync(Mixed, (window, view) =>
        {
            var box = window.GetVisualDescendants().OfType<TextBox>().Single();
            Drag(window, AfterTheEnd(window, view, "The intro"), box.TranslatePoint(new Point(8, 8), window)!.Value);
            return Task.FromResult(Selected(view));
        }, withTextBox: true);

        Assert.Equal(["The heading", "first item", "second item", "The outro text."], selected);
    }

    [Fact]
    public async Task Copy_OfADragFromAfterTheEndOfATextIntoTheNext_CopiesThePartOfTheNext()
    {
        var copied = await ShowAsync(Mixed, async (window, view) =>
        {
            Drag(window, AfterTheEnd(window, view, "The intro"), EdgeAt(window, view, "The heading", " heading"));
            Press(window, Keys.Copy[0]);
            return await ClipboardAsync(window);
        });

        Assert.Equal("The", copied);
    }

    [Fact]
    public async Task Drag_UpwardsSelectsTheSameTexts()
    {
        var selected = await RenderAsync(Mixed, (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "The outro", " text."), EdgeAt(window, view, "The intro", "intro"));
            return Selected(view);
        });

        Assert.Equal(MixedSelection, selected);
    }

    [Fact]
    public async Task Drag_OverATableAndACodeBlock_SelectsEveryCellAndTheCode()
    {
        const string markdown = "Before the table.\n\n| Name | Size |\n|---|---|\n| One | 2 |\n\n```\nborea install\n```\n\nAfter the code.";

        var selected = await RenderAsync(markdown, (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "Before", "table"), EdgeAt(window, view, "After", " the"));
            return Selected(view);
        });

        Assert.Equal(["table.", "Name", "Size", "One", "2", "borea install", "After"], selected.Select(text => text.TrimEnd('\n')));
    }

    [Fact]
    public async Task Drag_BackIntoTheTextItStartedIn_LeavesOnlyThatText()
    {
        var selected = await RenderAsync(Mixed, (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "The intro", "intro"), EdgeAt(window, view, "The outro", " text."), EdgeAt(window, view, "The intro", " text."));
            return Selected(view);
        });

        Assert.Equal(["intro"], selected);
    }

    [Fact]
    public async Task Drag_IntoTheGapBelowAText_EndsAtTheEndOfThatText()
    {
        var selected = await RenderAsync(Mixed, (window, view) =>
        {
            var heading = Find(view, "The heading");
            var gap = heading.TranslatePoint(new Point(4, heading.Bounds.Height + view.Spacing / 2), window)!.Value;
            Drag(window, EdgeAt(window, view, "The intro", "intro"), gap);
            return Selected(view);
        });

        Assert.Equal(["intro text.", "The heading"], selected);
    }

    [Fact]
    public async Task Press_AfterASelection_ClearsEveryOtherText()
    {
        var selected = await RenderAsync(Mixed, (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "The intro", "intro"), EdgeAt(window, view, "The outro", " text."));
            Click(window, EdgeAt(window, view, "The heading", "heading"));
            return Selected(view);
        });

        Assert.Empty(selected);
    }

    [Fact]
    public async Task FocusThatLeavesTheView_ClearsTheSelection()
    {
        var (before, after) = await ShowAsync(Mixed, (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "The intro", "intro"), EdgeAt(window, view, "The outro", " text."));
            var before = Selected(view).Count;
            window.GetVisualDescendants().OfType<TextBox>().Single().Focus();
            Dispatcher.UIThread.RunJobs();
            return Task.FromResult((before, Selected(view)));
        }, withTextBox: true);

        Assert.Equal(MixedSelection.Length, before);
        Assert.Empty(after);
    }

    [Fact]
    public async Task CopyOfATextInTheMiddle_CopiesTheWholeSelection()
    {
        var copied = await ShowAsync(Mixed, async (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "The intro", "intro"), EdgeAt(window, view, "The outro", " text."));
            Find(view, "first item").Copy();
            Dispatcher.UIThread.RunJobs();
            return await ClipboardAsync(window);
        });

        Assert.Equal(string.Join("\n\n", MixedSelection), copied);
    }

    [Fact]
    public async Task SelectAll_InAText_SelectsTheWholeView()
    {
        var selected = await RenderAsync(Mixed, (window, view) =>
        {
            Click(window, EdgeAt(window, view, "first item", "item"));
            Press(window, Keys.SelectAll[0]);
            return Selected(view);
        });

        Assert.Equal(["The intro text.", "The heading", "first item", "second item", "The outro text."], selected);
    }

    [Fact]
    public async Task LinkInADragOverTwoTexts_OpensNothing_AndOpensOnAClick()
    {
        var viewModel = new MainViewModel();
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        const string markdown = "Read [the guide](https://example.com/guide) first.\n\nThe outro text.";

        var (afterDrag, selected) = await RenderAsync(markdown, (window, view) =>
        {
            Drag(window, EdgeAt(window, view, "Read", "guide"), EdgeAt(window, view, "The outro", " text."));
            var afterDrag = opened.ToList();
            var selected = Selected(view);
            // away from the press of the drag, so the click does not count as a double click
            Click(window, EdgeAt(window, view, "Read", "the guide"));
            return (afterDrag, selected);
        }, viewModel);

        Assert.Empty(afterDrag);
        Assert.Equal(["guide first.", "The outro"], selected);
        Assert.Equal(["https://example.com/guide"], opened);
    }
}
