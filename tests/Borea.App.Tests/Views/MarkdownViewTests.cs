using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Core.Index;
using Borea.Core.Listings;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed partial class MarkdownViewTests
{
    private static DescriptionImages Images(params DescriptionImage[] records) =>
        new(new MainViewModel(), new ContentImages(icon: null, records));

    private static DescriptionImage Record(string id, string url = "https://images.example/shot.png") =>
        new(id, url, new string('A', 64), 1600, 900, 400_000);

    /// <summary>Shows the text in a window and runs <paramref name="probe"/> while the window is open.</summary>
    private static Task<T> RenderAsync<T>(string markdown, Func<Window, MarkdownView, T> probe, DescriptionImages? images = null, MainViewModel? viewModel = null, double width = 800) =>
        HeadlessApp.RunAsync(() =>
        {
            var view = new MarkdownView { Markdown = markdown, Images = images };
            var window = new Window { Width = width, Height = 600, Content = view, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                return Task.FromResult(probe(window, view));
            }
            finally
            {
                window.Close();
            }
        });

    private static List<TextBlock> TextBlocks(Visual view) => view.GetVisualDescendants().OfType<TextBlock>().ToList();

    private static string Text(TextBlock text) => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : text.Text ?? string.Empty;

    private static TextBlock Find(Visual view, string text) => Assert.Single(TextBlocks(view), block => Text(block).Contains(text, StringComparison.Ordinal));

    private static Point PointAt(Window window, TextBlock text, int index) =>
        text.TranslatePoint(text.TextLayout.HitTestTextPosition(index).Center, window)!.Value;

    private static void Click(Window window, TextBlock text, int index)
    {
        var point = PointAt(window, text, index);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static (MainViewModel ViewModel, List<string> Opened) Opener()
    {
        var viewModel = new MainViewModel();
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        return (viewModel, opened);
    }

    [Fact]
    public async Task Link_ClickOpensItsAddressAndHoverShowsIt()
    {
        var (viewModel, opened) = Opener();

        var (tip, underlined, accent) = await RenderAsync("Read [the guide](https://example.com/guide) first.", (window, view) =>
        {
            var text = Assert.Single(TextBlocks(view));
            var link = Text(text).IndexOf("guide", StringComparison.Ordinal);
            window.MouseMove(PointAt(window, text, link));
            var tip = ToolTip.GetTip(text);
            Click(window, text, link);
            Click(window, text, Text(text).IndexOf("first", StringComparison.Ordinal));
            var run = text.Inlines!.OfType<Run>().Single(item => item.Text == "the guide");
            return (tip, ReferenceEquals(run.TextDecorations, TextDecorations.Underline), run.Foreground == MarkdownView.Resource<IBrush>("Brush.Accent"));
        }, viewModel: viewModel);

        Assert.Equal(["https://example.com/guide"], opened);
        Assert.Equal("https://example.com/guide", tip);
        Assert.True(underlined);
        Assert.True(accent);
    }

    [Fact]
    public async Task LinkOnALowerLineOfAWrappedParagraph_OpensOnClick()
    {
        var (viewModel, opened) = Opener();

        var line = await RenderAsync("This paragraph is long enough to wrap onto more than one line in a narrow view, and [the guide](https://example.com/guide) is near its end.", (window, view) =>
        {
            var text = Assert.Single(TextBlocks(view));
            var link = Text(text).IndexOf("guide", StringComparison.Ordinal);
            Click(window, text, link);
            return text.TextLayout.GetLineIndexFromCharacterIndex(link, false);
        }, viewModel: viewModel, width: 300);

        Assert.True(line > 0, "The link is on the first line.");
        Assert.Equal(["https://example.com/guide"], opened);
    }

    [Fact]
    public async Task BareUrl_IsALinkByTheSameRule()
    {
        var (viewModel, opened) = Opener();

        await RenderAsync("Get it at https://example.com/mod or www.example.org now.", (window, view) =>
        {
            var text = Assert.Single(TextBlocks(view));
            Click(window, text, Text(text).IndexOf("example.com", StringComparison.Ordinal));
            Click(window, text, Text(text).IndexOf("www", StringComparison.Ordinal));
            return true;
        }, viewModel: viewModel);

        Assert.Equal(["https://example.com/mod", "https://www.example.org/"], opened);
    }

    [Theory]
    [InlineData("Run [this](javascript:alert(1)) now")]
    [InlineData("Run [this](file:///C:/Windows/notepad.exe) now")]
    [InlineData("Run [this](mailto:someone@example.com) now")]
    [InlineData("Run [this](ftp://example.com/file) now")]
    [InlineData("Run [this](docs/readme.md) now")]
    [InlineData("Run <mailto:this@example.com> now")]
    [InlineData("Run this@example.com now")]
    public async Task LinkWithAnotherScheme_StaysTextAndOpensNothing(string markdown)
    {
        var (viewModel, opened) = Opener();

        var (shown, decorated, tip) = await RenderAsync(markdown, (window, view) =>
        {
            var text = Assert.Single(TextBlocks(view));
            var middle = Text(text).IndexOf("this", StringComparison.Ordinal) + 2;
            window.MouseMove(PointAt(window, text, middle));
            var tip = ToolTip.GetTip(text);
            Click(window, text, middle);
            return (Text(text), text.Inlines!.OfType<Run>().Any(run => run.TextDecorations is not null), tip);
        }, viewModel: viewModel);

        Assert.StartsWith("Run ", shown, StringComparison.Ordinal);
        Assert.EndsWith(" now", shown, StringComparison.Ordinal);
        Assert.False(decorated);
        Assert.Null(tip);
        Assert.Empty(opened);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("C:\\Windows\\notepad.exe")]
    [InlineData("notepad.exe")]
    public void OpenMarkdownLink_AnotherScheme_OpensNothing(string url)
    {
        var (viewModel, opened) = Opener();

        viewModel.OpenMarkdownLink(url);

        Assert.Empty(opened);
    }

    [Fact]
    public void OpenMarkdownLink_CannotOpen_ShowsAToast()
    {
        var viewModel = new MainViewModel { OpenWithSystem = _ => throw new Win32Exception("No browser is set.") };

        viewModel.OpenMarkdownLink("https://example.com/guide");

        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(viewModel.Localization.FormatToastOpenFailed("https://example.com/guide"), toast.Message);
    }

    [Fact]
    public async Task EscapesAndEntities_AreResolved()
    {
        var (shown, italic) = await RenderAsync(@"Use \_name\_ and \*star\* &amp; &lt;tag&gt; &#35;1 \\ end", (_, view) =>
        {
            var text = Assert.Single(TextBlocks(view));
            return (Text(text), text.Inlines!.OfType<Run>().Any(run => run.FontStyle == FontStyle.Italic));
        });

        Assert.Equal(@"Use _name_ and *star* & <tag> #1 \ end", shown);
        Assert.False(italic);
    }

    [Fact]
    public async Task TrailingSpacesAndBackslash_BreakTheLine()
    {
        var (shown, lines) = await RenderAsync("one  \ntwo\\\nthree\nfour", (_, view) =>
        {
            var text = Assert.Single(TextBlocks(view));
            return (Text(text), text.TextLayout.TextLines.Count);
        });

        Assert.Equal("one\ntwo\nthree four", shown);
        Assert.Equal(3, lines);
    }

    [Fact]
    public async Task ThematicBreak_DrawsADivider()
    {
        var children = await RenderAsync("Above\n\n---\n\nBelow", (_, view) => view.Children.Select(child => child switch
        {
            TextBlock text => Text(text),
            Border { Height: 1, Background: not null } => "divider",
            _ => child.GetType().Name,
        }).ToList());

        Assert.Equal(["Above", "divider", "Below"], children);
    }

    [Fact]
    public async Task Table_DrawsAGridWhoseCellsWrap()
    {
        const string markdown = """
            | File | SHA-256 |
            | --- | --- |
            | AdvancedFlightComputer-0.9.1.zip | `cb6f786bb37e99e6993bfc4dd74ac012e0e6d21ccc5411a260c00c9ed90907cd` |
            | Short.zip | |
            """;

        var (columns, cells, texts, hashLines, header, fits) = await RenderAsync(markdown, (_, view) =>
        {
            var grid = Assert.Single(view.GetVisualDescendants().OfType<Grid>());
            var hash = Find(view, "cb6f786b");
            return (
                grid.ColumnDefinitions.Count,
                grid.Children.Count,
                TextBlocks(view).Select(Text).ToList(),
                hash.TextLayout.TextLines.Count,
                Find(view, "File").FontWeight,
                grid.Bounds.Width <= view.Bounds.Width);
        }, width: 360);

        Assert.Equal(2, columns);
        Assert.Equal(6, cells);
        Assert.Equal(["File", "SHA-256", "AdvancedFlightComputer-0.9.1.zip", "cb6f786bb37e99e6993bfc4dd74ac012e0e6d21ccc5411a260c00c9ed90907cd", "Short.zip"], texts);
        Assert.True(hashLines > 1);
        Assert.Equal(FontWeight.SemiBold, header);
        Assert.True(fits);
    }

    [Fact]
    public async Task NestedListItems_AreIndentedByTheirLevel()
    {
        var (left, numbers) = await RenderAsync("- top\n  - nested\n    - deeper\n- second\n\n3. three\n4. four", (_, view) =>
        {
            double Left(string text) => Find(view, text).TranslatePoint(default, view)!.Value.X;
            var left = new[] { Left("top"), Left("nested"), Left("deeper"), Left("second") };
            var markers = TextBlocks(view).Select(Text).Where(text => text.EndsWith("  ", StringComparison.Ordinal)).Select(text => text.Trim()).ToList();
            return (left, markers);
        });

        Assert.Equal(left[0], left[3]);
        Assert.True(left[1] - left[0] >= 20);
        Assert.Equal(left[1] - left[0], left[2] - left[1], 0.5);
        Assert.Equal(["3.", "4."], numbers.Where(marker => char.IsDigit(marker[0])));
    }

    [Fact]
    public async Task HeadingsKeepTheirLevelAndCodeItsLinesInTheMonoFont()
    {
        var blocks = await RenderAsync("# One\ntext `inline`\n### Three\n```json\n{ \"a\": 1 }\n  # not a heading\n```\n\n    indented", (_, view) =>
            TextBlocks(view).Select(text => (Text(text), string.Join(' ', text.Classes), text.FontFamily == MarkdownView.MonoFont)).ToList());

        Assert.Equal(
            [
                ("One", "heading-lg", false),
                ("text inline", string.Empty, false),
                ("Three", "heading-sm", false),
                ("{ \"a\": 1 }\n  # not a heading", string.Empty, true),
                ("indented", string.Empty, true),
            ],
            blocks);
    }

    [Fact]
    public async Task Headings_CanBeSelectedAndKeepTheLookOfTheirTextClass()
    {
        var headings = await RenderAsync("# Large\n\n## Medium\n\n### Small", (_, view) =>
            TextBlocks(view).Select(text => (text is SelectableTextBlock, Text(text), string.Join(' ', text.Classes), text.FontSize, text.FontWeight)).ToList());

        Assert.Equal(
            [
                (true, "Large", "heading-lg", 24, FontWeight.SemiBold),
                (true, "Medium", "heading-md", 16, FontWeight.SemiBold),
                (true, "Small", "heading-sm", 14, FontWeight.SemiBold),
            ],
            headings);
    }

    [Fact]
    public async Task Code_TakesTheSurfacesOfTheTheme()
    {
        var (inline, block) = await RenderAsync("text `inline`\n\n```\nblock\n```", (_, view) =>
        {
            var texts = TextBlocks(view).ToList();
            var run = texts[0].Inlines!.OfType<Run>().Single(item => item.FontFamily == MarkdownView.MonoFont);
            return (ReferenceEquals(run.Background, MarkdownView.Resource<IBrush>("Brush.SurfaceRaised")),
                ReferenceEquals(texts[1].Background, MarkdownView.Resource<IBrush>("Brush.SurfaceHeader")));
        });

        Assert.True(inline, "inline code takes Brush.SurfaceRaised");
        Assert.True(block, "a code block takes Brush.SurfaceHeader");
    }

    [Fact]
    public async Task InlineHtml_BreaksAtBrAndKeepsOnlyTheText()
    {
        var shown = await RenderAsync("""First<br>second <b>bold</b> <span class="x">kept</span><!-- note --> <img src="https://example.com/a.png" alt="Logo &amp; name">""", (_, view) =>
            Text(Assert.Single(TextBlocks(view))));

        Assert.Equal("First\nsecond bold kept Logo & name", shown);
    }

    [Fact]
    public async Task HtmlBlock_ShowsItsTextAndNoTags()
    {
        const string markdown = """
            <div align="center">
              <img src="https://example.com/banner.png" alt="Banner">
              <p>Made by <a href="https://example.com">me</a>
              &amp; friends</p>
            </div>

            <!-- a hidden note -->

            <script>alert(1)</script>

            After
            """;

        var texts = await RenderAsync(markdown, (_, view) => TextBlocks(view).Select(Text).ToList());

        Assert.Equal(["Banner\nMade by me & friends", "After"], texts);
    }

    [Fact]
    public async Task LinkInAListAQuoteAndATableCell_OpensOnClick()
    {
        var (viewModel, opened) = Opener();

        await RenderAsync("- go [x](https://example.com/list) now\n\n> go [x](https://example.com/quote) now\n\n| a |\n| - |\n| go [x](https://example.com/cell) now |", (window, view) =>
        {
            foreach (var text in TextBlocks(view).Where(block => Text(block).StartsWith("go ", StringComparison.Ordinal)))
                Click(window, text, Text(text).IndexOf('x', StringComparison.Ordinal));
            return true;
        }, viewModel: viewModel);

        Assert.Equal(["https://example.com/list", "https://example.com/quote", "https://example.com/cell"], opened);
    }

    [Theory]
    [InlineData("quotes")]
    [InlineData("emphasis")]
    public async Task NestingDeeperThanTheBound_ShowsTheTextAsItsSource(string kind)
    {
        var markdown = kind == "quotes"
            ? string.Concat(Enumerable.Repeat("> ", 130)) + "deep"
            : new string('*', 8000) + "deep" + new string('*', 8000);

        var texts = await RenderAsync(markdown, (_, view) => TextBlocks(view).Select(Text).ToList());

        Assert.Equal([markdown], texts);
    }

    [Theory]
    [InlineData("<a ")]
    [InlineData("<!--")]
    public async Task HtmlBlockWithoutClosingBrackets_ReadsQuicklyAndHidesAnOpenComment(string repeated)
    {
        var markdown = "<div>\n" + string.Concat(Enumerable.Repeat(repeated, 20000));

        var (blocks, elapsed) = await HeadlessApp.RunAsync(() =>
        {
            var clock = Stopwatch.StartNew();
            var view = new MarkdownView { Markdown = markdown };
            return Task.FromResult((view.Children.Count, clock.Elapsed));
        });

        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"The HTML block took {elapsed}.");
        Assert.Equal(repeated == "<a " ? 1 : 0, blocks);
    }

    [Fact]
    public async Task ParagraphText_CanBeSelectedAndCopied()
    {
        var (selected, copied) = await HeadlessApp.RunAsync(async () =>
        {
            var view = new MarkdownView { Markdown = "Select **this** paragraph." };
            var window = new Window { Width = 800, Height = 200, Content = view };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var text = Assert.IsType<SelectableTextBlock>(Assert.Single(TextBlocks(view)));
                var length = Text(text).Length;
                window.MouseDown(PointAt(window, text, 0), MouseButton.Left);
                window.MouseMove(PointAt(window, text, length - 1) + new Point(8, 0), RawInputModifiers.LeftMouseButton);
                window.MouseUp(PointAt(window, text, length - 1) + new Point(8, 0), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                var selected = text.SelectedText;
                text.Copy();
                Dispatcher.UIThread.RunJobs();
                return (selected, await window.Clipboard!.TryGetTextAsync());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal("Select this paragraph.", selected);
        Assert.Equal("Select this paragraph.", copied);
    }

    [Fact]
    public async Task ReferenceToARecord_PlacesThatImageWithoutLoadingIt()
    {
        var images = Images(Record("settings-window"));

        var (parts, loaded) = await RenderAsync("Before ![The settings window](ksa-image:settings-window) after", (_, view) =>
            (view.Children.Select(Describe).ToList(), view.GetVisualDescendants().OfType<ListingImageView>().Single().Image!.IsLoaded), images);

        Assert.Equal(["Before ", "image settings-window The settings window", " after"], parts);
        Assert.False(loaded);
    }

    [Fact]
    public async Task ReferenceToAnIdWithoutARecord_ShowsAMissingImageAndTheRest()
    {
        var parts = await RenderAsync("![The old map](ksa-image:Settings-Window) Still shown.", (_, view) => view.Children.Select(Describe).ToList(), Images(Record("settings-window")));

        Assert.Equal(["missing The old map", " Still shown."], parts);
    }

    [Fact]
    public async Task AnyOtherImage_ShowsItsAlternativeTextAndLinksStayLinks()
    {
        var (parts, link) = await RenderAsync(
            """![Remote shot](https://images.example/shot.png) <img src="ksa-image:shot" alt="Html shot"> [shot](https://images.example/shot.png)![](KSA-IMAGE:shot)""",
            (_, view) => (view.Children.Select(Describe).ToList(), Find(view, "Remote").Inlines!.OfType<Run>().Single(run => run.TextDecorations is not null).Text),
            Images(Record("shot")));

        Assert.Equal(["Remote shot Html shot shot"], parts);
        Assert.Equal("shot", link);
    }

    [Fact]
    public async Task ImageInsideBold_KeepsTheBoldTextAndTheImage()
    {
        var (parts, bold) = await RenderAsync("**Screenshot ![Map](ksa-image:map)** and *![Shot](ksa-image:shot)*", (_, view) =>
            (view.Children.Select(Describe).ToList(), Find(view, "Screenshot").Inlines!.OfType<Run>().Single().FontWeight),
            Images(Record("map"), Record("shot")));

        Assert.Equal(["Screenshot ", "image map Map", " and ", "image shot Shot"], parts);
        Assert.Equal(FontWeight.Bold, bold);
    }

    [Fact]
    public async Task Changelog_ShowsEveryImageAsItsAlternativeText()
    {
        var parts = await RenderAsync("Fixed the ![settings window](ksa-image:settings-window).", (_, view) => view.Children.Select(Describe).ToList());

        Assert.Equal(["Fixed the settings window."], parts);
    }

    /// <summary>Both sides are compared with the ids that the text holds, so an image that only one side reads fails the test.</summary>
    [Theory]
    [InlineData("![Map](ksa-image:map) ![Shot][shot]\n\n[shot]: <ksa-image:shot> \"Title\"", "map shot")]
    [InlineData("[![Logo][logo]][site] and ![escaped](ksa-image:my\\_shot) ![entity](ksa-image&#58;map)\n\n[logo]: ksa-image:logo\n[site]: https://example.com", "logo my_shot map")]
    [InlineData("- item ![Nested](ksa-image:in-list)\n  > ![Quoted](ksa-image:in-quote)\n\n| a | b |\n| - | - |\n| ![Cell](ksa-image:in-cell) | `![code](ksa-image:in-code)` |", "in-list in-quote in-cell")]
    [InlineData("```\n![code](ksa-image:in-fence)\n```\n\n\\![escaped](ksa-image:not-an-image) <img src=\"ksa-image:html\"> ![Last](ksa-image:last)", "last")]
    public async Task ImageReference_ReadsTheSameInTheAppAndInTheListingEditor(string markdown, string ids)
    {
        var expected = ids.Split(' ');
        var images = Images([.. expected.Select(id => Record(id))]);

        var shown = await RenderAsync(markdown, (_, view) => view.GetVisualDescendants().OfType<ListingImageView>().Select(image => ((DescriptionImage)image.Image!.Record).Id).ToList(), images);

        Assert.Equal(expected, MarkdownImages.References(markdown));
        Assert.Equal(expected, shown);
    }

    [Fact]
    public async Task Description_DrawsEmphasisFromTheBundledFacesWithoutSimulation()
    {
        var faces = await HeadlessApp.RunAsync(() =>
        {
            var view = new MarkdownView { Markdown = "## Heading **bold** *italic*\n\nPlain **bold** *italic* ***bold italic***" };
            var window = new Window { Width = 800, Height = 200, Content = view };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame()!;
                var faces = view.Children.OfType<TextBlock>()
                    .SelectMany(text => text.TextLayout.TextLines)
                    .SelectMany(line => line.TextRuns)
                    .OfType<ShapedTextRun>()
                    .Where(run => !string.IsNullOrWhiteSpace(run.Text.ToString()))
                    .Select(run => run.GlyphRun.GlyphTypeface)
                    .Select(face => $"{face.FamilyName} {face.Weight} {face.Style} {face.FontSimulations}")
                    .ToList();
                return Task.FromResult(faces);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(
            [
                "IBM Plex Sans SmBld DemiBold Normal None",
                "IBM Plex Sans Bold Normal None",
                "IBM Plex Sans SmBld DemiBold Italic None",
                "IBM Plex Sans Normal Normal None",
                "IBM Plex Sans Bold Normal None",
                "IBM Plex Sans Normal Italic None",
                "IBM Plex Sans Bold Italic None",
            ],
            faces);
    }

    [Fact]
    public async Task IndexTexts_RenderWithoutMarkdownLeftOver()
    {
        var texts = IndexTexts();
        Assert.Equal(23, texts.Count(text => text.Kind == "description"));
        Assert.Equal(76, texts.Count(text => text.Kind == "release notes"));

        var leftovers = await HeadlessApp.RunAsync(() =>
        {
            var found = new List<string>();
            foreach (var (kind, where, markdown) in texts)
            {
                var view = new MarkdownView { Markdown = markdown };
                var prose = view.GetLogicalDescendants().OfType<TextBlock>().Select(Prose).ToList();
                if (!prose.Any(text => text.Trim().Length > 0))
                    found.Add($"{kind} {where}: no text");

                foreach (var text in prose)
                {
                    foreach (Match match in Leftover().Matches(text))
                        found.Add($"{kind} {where}: '{match.Value}' in '{text}'");
                }
            }

            return Task.FromResult(found);
        });

        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task Edit_RebuildsOnlyTheChangedBlocksAndDrawsWhatAFreshViewDraws()
    {
        string[] edits =
        [
            "Intro\n\n![Shot](ksa-image:shot)\n\n- one\n- two\n\nTyped",
            "Intro\n\n![Shot](ksa-image:shot)\n\n- one\n- two\n\nTyped more",
            "Intro\n\nNew block\n\n![Shot](ksa-image:shot)\n\n- one\n- two\n\nTyped more",
            "Intro ![Shot](ksa-image:shot) after\n\n- one\n- two and three\n\n```\ncode\n```",
            "Intro",
            "",
        ];

        var steps = await HeadlessApp.RunAsync(() =>
        {
            var images = Images(Record("shot"));
            var view = new MarkdownView { Images = images, Markdown = edits[0] };
            var steps = new List<(List<Control> Before, List<Control> After, List<string> Drawn, List<string> Fresh)>();
            foreach (var edit in edits.Skip(1))
            {
                var before = view.Children.ToList();
                view.Markdown = edit;
                steps.Add((before, view.Children.ToList(), Drawn(view), Drawn(new MarkdownView { Images = images, Markdown = edit })));
            }

            return Task.FromResult(steps);
        });

        foreach (var step in steps)
            Assert.Equal(step.Fresh, step.Drawn);

        var typed = steps[0];
        Assert.Equal(typed.Before.Take(3), typed.After.Take(3));
        Assert.NotSame(typed.Before[3], typed.After[3]);
        var inserted = steps[1];
        Assert.Same(inserted.Before[0], inserted.After[0]);
        Assert.Equal(inserted.Before.Skip(1), inserted.After.Skip(2));
        Assert.Empty(steps[^1].After);
    }

    /// <summary>A repeated block is part of both the equal start and the equal end, so the end must not count the blocks of the start again.</summary>
    [Theory]
    [InlineData("- a\n- a", "- a")]
    [InlineData("Same\n\nSame\n\nSame", "Same\n\nSame")]
    [InlineData("A", "A\n\nA")]
    [InlineData("A\n\nA", "A\n\nA\n\nA")]
    public async Task Edit_WhereTheEqualStartAndEndOverlap_DrawsWhatAFreshViewDraws(string before, string after)
    {
        var (drawn, fresh) = await EditAsync(before, after);

        Assert.Equal(fresh, drawn);
    }

    /// <summary>
    /// The indent before a block and the blank lines at the end of a code block without a closing fence are outside the span of the block,
    /// but they change what it draws.
    /// </summary>
    [Theory]
    [InlineData("  ```\n  code\n  ```", "```\n  code\n  ```")]
    [InlineData("```\n  code\n  ```", "  ```\n  code\n  ```")]
    [InlineData("Intro\n\n  ```\n  code\n  ```\n\nEnd", "Intro typed\n\n```\n  code\n  ```\n\nEnd")]
    [InlineData(" - a\n\n      code", "- a\n\n      code")]
    [InlineData(" 1. a\n\n       code", "1. a\n\n       code")]
    [InlineData("x\n\n```\ncode", "x\n\n```\ncode\n\n\n")]
    public async Task Edit_OfTheIndentOrTheTrailingBlankLines_DrawsWhatAFreshViewDraws(string before, string after)
    {
        var (drawn, fresh) = await EditAsync(before, after);

        Assert.Equal(fresh, drawn);
    }

    /// <summary>A link reference definition changes what a block that uses it draws, also when the source of that block stays the same.</summary>
    [Theory]
    [InlineData("[x][r] and more\n\nEnd", "[x][r] and more\n\nEnd\n\n[r]: https://example.com/r")]
    [InlineData("[x][r] and more\n\n[r]: https://example.com/r\n\nEnd", "[x][r] and more\n\nEnd")]
    [InlineData("[x][r] and more\n\n[r]: https://example.com/r", "[x][r] and more\n\n[r]: javascript:alert(1)")]
    public async Task Edit_OfALinkReferenceDefinition_DrawsWhatAFreshViewDraws(string before, string after)
    {
        var (drawn, fresh) = await EditAsync(before, after);

        Assert.Equal(fresh, drawn);
    }

    /// <summary>A text too deep to parse shows as one paragraph that has no source span, so every edit builds that paragraph again.</summary>
    [Fact]
    public async Task Edit_OfATextTooDeepToParse_DrawsWhatAFreshViewDraws()
    {
        var deep = string.Concat(Enumerable.Repeat("> ", 40));

        var (drawn, fresh) = await EditAsync(deep + "first", deep + "second");

        Assert.Equal(fresh, drawn);
        Assert.EndsWith("second]", drawn.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewImages_RebuildEveryBlock()
    {
        var (before, after, url) = await HeadlessApp.RunAsync(() =>
        {
            var view = new MarkdownView { Images = Images(Record("shot")), Markdown = "Intro\n\n![Shot](ksa-image:shot)" };
            var before = view.Children.ToList();
            view.Images = Images(Record("shot", "https://images.example/shot-v2.png"));
            return Task.FromResult((before, view.Children.ToList(), Drawn(view).Single(line => line.StartsWith("ListingImageView", StringComparison.Ordinal))));
        });

        Assert.Equal(2, after.Count);
        Assert.DoesNotContain(after, before.Contains);
        Assert.EndsWith("https://images.example/shot-v2.png", url, StringComparison.Ordinal);
    }

    /// <summary>What a view draws after its text changed from <paramref name="before"/> to <paramref name="after"/>, and what a fresh view of <paramref name="after"/> draws.</summary>
    private static Task<(List<string> Drawn, List<string> Fresh)> EditAsync(string before, string after) =>
        HeadlessApp.RunAsync(() =>
        {
            var view = new MarkdownView { Markdown = before };
            view.Markdown = after;
            return Task.FromResult((Drawn(view), Drawn(new MarkdownView { Markdown = after })));
        });

    /// <summary>The text of a block outside code, where Markdown characters are part of the text.</summary>
    private static string Prose(TextBlock text) =>
        text.FontFamily == MarkdownView.MonoFont || text.Inlines is not { Count: > 0 } inlines
            ? string.Empty
            : string.Concat(inlines.OfType<Run>().Where(run => run.FontFamily != MarkdownView.MonoFont).Select(run => run.Text));

    /// <summary>The descriptions and release notes of the published index on 2026-10-02, cut down to their Markdown.</summary>
    private static List<(string Kind, string Where, string Markdown)> IndexTexts()
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "Fixtures", "index-markdown-2026-10-02.json")));
        var texts = new List<(string, string, string)>();
        foreach (var listing in snapshot.RootElement.GetProperty("listings").EnumerateArray())
        {
            var id = listing.GetProperty("id").GetString()!;
            if (listing.GetProperty("authored").TryGetProperty("description", out var description))
                texts.Add(("description", id, description.GetString()!));
            foreach (var release in listing.GetProperty("releases").EnumerateArray())
                texts.Add(("release notes", $"{id} {release.GetProperty("version").GetString()}", release.GetProperty("changelog_text").GetString()!));
        }

        foreach (var pack in snapshot.RootElement.GetProperty("packs").EnumerateArray())
        {
            foreach (var version in pack.GetProperty("versions").EnumerateArray())
                texts.Add(("description", pack.GetProperty("id").GetString()!, version.GetProperty("authored").GetProperty("description").GetString()!));
        }

        return texts;
    }

    private static string Describe(Control child) => child switch
    {
        TextBlock text => Text(text),
        Border { Child: ListingImageView image } => $"image {((DescriptionImage)image.Image!.Record).Id} {AutomationName(image)}",
        StackPanel { Children: [Border { Child: ListingImageView image }, ..] } => $"image {((DescriptionImage)image.Image!.Record).Id} {AutomationName(image)}",
        Border placeholder => $"missing {AutomationName(placeholder)}",
        _ => child.GetType().Name,
    };

    private static string? AutomationName(Control control) => AutomationProperties.GetName(control);

    [GeneratedRegex(@"\*\*|__|\]\(|!\[|\]\[|^#{1,6}\s|^\s*>|^\s*([-+*]|\d+[.)])\s|^\s*([-*_])(\s*\2){2,}\s*$|\|\s*:?-{3,}|^\s*\||```|~~~|</?[A-Za-z][^>]*>|&#?[A-Za-z0-9]+;|\\[!-/:-@\[-`{-~]", RegexOptions.Multiline)]
    private static partial Regex Leftover();

    /// <summary>Every control the view built, with its classes, its text with the style of each run, and the image or name it shows.</summary>
    internal static List<string> Drawn(MarkdownView view) =>
        view.GetLogicalDescendants().OfType<Control>().Select(control => control switch
        {
            TextBlock text => $"{Kind(text)} {Inlines(text)}{text.Text}",
            ListingImageView image => $"{Kind(image)} {image.Image?.Record.Url}",
            _ => $"{Kind(control)} {AutomationProperties.GetName(control)}",
        }).ToList();

    private static string Kind(Control control) => $"{control.GetType().Name}.{string.Join('.', control.Classes)} {control.Opacity}";

    private static string Inlines(TextBlock text) => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.Select(Inline)) : string.Empty;

    private static string Inline(Inline inline) => inline is Run run ? $"{Style(run)}[{run.Text}]" : inline.GetType().Name;

    /// <summary>The bold, the italic, the underline of a link and the mono font of code, which are the styles the view gives a run.</summary>
    private static string Style(Run run) => string.Concat(
        run.FontWeight == FontWeight.Bold ? "b" : string.Empty,
        run.FontStyle == FontStyle.Italic ? "i" : string.Empty,
        run.TextDecorations is not null ? "u" : string.Empty,
        run.FontFamily == MarkdownView.MonoFont ? "m" : string.Empty);
}
