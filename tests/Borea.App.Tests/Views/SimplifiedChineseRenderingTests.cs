using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Borea.App.Localization;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.App.Views.Settings;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class SimplifiedChineseRenderingTests
{
    public static TheoryData<string, double, string> Pages
    {
        get
        {
            var data = new TheoryData<string, double, string>();
            foreach (var page in new[] { "Home", "Discover", "Library", "Listing", "General", "Game", "About", "Help", "Content", "Pack", "Instance", "Create" })
            {
                data.Add(page, 860d, "Dark");
                data.Add(page, 1280d, "Light");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task MainPages_RenderChineseWithoutMissingGlyphs(string pageName, double width, string theme)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "flight-pack", "Flight Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.9")))));
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        await harness.ViewModel.OpenListingAsync();
        if (pageName == "Content")
            await harness.ViewModel.OpenContentAsync(harness.ViewModel.DiscoverItems.First());
        if (pageName == "Pack")
        {
            harness.ViewModel.ShowDiscoverModpacksCommand.Execute(null);
            await harness.ViewModel.DiscoverPacks.Single().OpenCommand.ExecuteAsync(null);
        }
        if (pageName == "Instance")
        {
            await harness.Services.Instances.CreateAsync("\u4E2D\u6587\u6D4B\u8BD5\u5B9E\u4F8B", InstanceSource.Custom.Value);
            await harness.ViewModel.LoadAsync();
            await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        }

        await HeadlessApp.RunAsync(harness, async () =>
        {
            var original = CultureInfo.CurrentUICulture;
            var originalTheme = Application.Current!.RequestedThemeVariant;
            harness.Localization.TrySetCulture("zh-Hans");
            if (pageName == "Create")
                harness.ViewModel.BeginCreateInstanceCommand.Execute(null);
            Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            Control page = pageName switch
            {
                "Home" => new HomePage(),
                "Discover" => new DiscoverPage(),
                "Library" => new LibraryPage(),
                "Listing" => new ListingPage(),
                "General" => new GeneralSettings(),
                "Game" => new GameSettings(),
                "About" => new AboutSettings(),
                "Content" => new Borea.App.Views.Pages.ContentPage(),
                "Pack" => new PackPage(),
                "Instance" => new InstancePage(),
                "Create" => new InstanceNameModal(),
                _ => new HelpSettings(),
            };
            var window = new Window { Width = width, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                await HeadlessApp.FramesAsync(2);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var folder = Environment.GetEnvironmentVariable("BOREA_LOCALIZATION_SCREENSHOTS");
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    using var stream = File.Create(Path.Combine(folder, $"zh-Hans-{pageName}-{width}-{theme}.png"));
                    frame.Save(stream, PngBitmapEncoderOptions.Default);
                }

                var chinese = page.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && text.Bounds.Width > 0 && text.Text?.Any(IsHan) == true)
                    .ToArray();
                Assert.NotEmpty(chinese);
                foreach (var text in chinese)
                {
                    AssertGlyphs(text);
                    if (text.TextTrimming == TextTrimming.None)
                    {
                        Assert.All(text.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, text.Text));
                        Assert.True(text.TextLayout.Height <= text.Bounds.Height + 1, $"Text is clipped vertically: {text.Text}");
                    }
                }

            }
            finally
            {
                window.Close();
                Resources.Culture = original;
                CultureInfo.CurrentUICulture = original;
                Application.Current!.RequestedThemeVariant = originalTheme;
            }

            return true;
        });
    }

    [Theory]
    [InlineData("IBM Plex Sans")]
    [InlineData("IBM Plex Mono")]
    public async Task ResourceCharacters_HaveRealGlyphsWithBundledFontFallback(string family)
    {
        var resourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "Borea.App", "Localization", "Resources", "Resources.zh-Hans.resx"));
        var values = XDocument.Load(resourcePath).Root!.Elements("data")
            .Select(element => (string)element.Element("value")!);
        var characters = new string(string.Concat(values).Where(character => character > 127 && !char.IsWhiteSpace(character)).Distinct().Order().ToArray());
        Assert.NotEmpty(characters);

        await HeadlessApp.RunAsync(() =>
        {
            var text = new TextBlock
            {
                Text = characters,
                FontFamily = new FontFamily($"avares://Borea.App/Assets/Fonts#{family}"),
                TextWrapping = TextWrapping.Wrap,
            };
            var window = new Window { Width = 860, Height = 832, Content = text };
            window.Show();
            try
            {
                window.UpdateLayout();
                AssertGlyphs(text);
            }
            finally
            {
                window.Close();
            }

            return Task.FromResult(true);
        });
    }

    private static bool IsHan(char character) => character is >= '\u3400' and <= '\u9fff';

    private static void AssertGlyphs(TextBlock text)
    {
        var runs = text.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
        Assert.NotEmpty(runs);
        foreach (var run in runs.Where(run => run.Text.ToString().Any(character => character > 127 && !char.IsWhiteSpace(character))))
            foreach (var glyph in run.GlyphRun.GlyphInfos)
                Assert.True(glyph.GlyphIndex != 0, $"Missing glyph in {run.Text} ({run.GlyphRun.GlyphTypeface.FamilyName})");
    }
}
