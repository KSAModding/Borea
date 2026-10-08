using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class UnexpectedErrorBarTests
{
    /// <summary>The lowest contrast that WCAG AA allows for body text.</summary>
    internal const double ReadableContrast = 4.5;

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Background_IsOpaque_SoThePageBelowDoesNotShowThrough(string theme)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));

        var (padding, expected, danger) = await HeadlessApp.RunAsync(() =>
        {
            Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var bar = new UnexpectedErrorBar { VerticalAlignment = VerticalAlignment.Top };
            // a color no theme uses, so any part of it in the bar means the bar lets the page through
            var window = new Window { Width = 800, Height = 300, DataContext = viewModel, Content = new Panel { Background = Brushes.Lime, Children = { bar } } };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame()!;
                using var buffer = frame.Lock();
                // one point in the left and one in the top padding, where no text or button is drawn
                var left = bar.TranslatePoint(new Point(8, bar.Bounds.Height / 2), window)!.Value;
                var top = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, 5), window)!.Value;
                Color[] padding = [Pixel(buffer, left), Pixel(buffer, top)];
                var expected = Over(Token(bar, "Color.DangerSoft"), Token(bar, "Color.Window"));
                return Task.FromResult((padding, expected, Token(bar, "Color.Danger")));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.All(padding, pixel => AssertClose(expected, pixel));
        Assert.All(padding, pixel => Assert.True(Contrast(danger, pixel) >= ReadableContrast, $"The error text has a contrast of {Contrast(danger, pixel):F2} in the {theme} theme."));
    }

    [Fact]
    public async Task Actions_CopyTheDetails_OpenTheReport_AndCloseTheBar()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var clipboard = new UnexpectedErrorTests.FakeWindowServices();
        viewModel.WindowServices = clipboard;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));

        await HeadlessApp.RunAsync(harness, () =>
        {
            var bar = new UnexpectedErrorBar();
            var window = new Window { Width = 800, Height = 300, DataContext = viewModel, Content = bar };
            window.Show();
            try
            {
                bar.UpdateLayout();
                // the click finds the button only in a rendered frame
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var buttons = bar.GetVisualDescendants().OfType<Button>().ToList();
                Click(window, buttons.Single(button => Equals(button.Content, harness.Localization.UnexpectedErrorCopyDetails)));
                Click(window, buttons.Single(button => Equals(button.Content, harness.Localization.UnexpectedErrorReport)));
                Click(window, buttons.Single(button => button.Command == viewModel.DismissUnexpectedErrorCommand));
                return Task.FromResult(true);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("InvalidOperationException: The instance is gone.", clipboard.CopiedText);
        Assert.StartsWith(MainViewModel.ReportBugUrl + "?title=", Assert.Single(opened));
        Assert.Null(viewModel.UnexpectedError);
    }

    [Fact]
    public async Task MainWindow_ShowsTheBar_OnlyWhileThereIsAnError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var (bars, visibleBefore, visibleWithError, visibleAfterDismiss) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var found = window.GetVisualDescendants().OfType<UnexpectedErrorBar>().ToList();
                var bar = found.Single();
                var before = bar.IsVisible;
                viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));
                Dispatcher.UIThread.RunJobs();
                var withError = bar.IsVisible;
                viewModel.DismissUnexpectedErrorCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                return Task.FromResult((found.Count, before, withError, bar.IsVisible));
            }
            finally
            {
                window.Close();
            }
        });

        // the window must use this control, because the tests of this class do not see an inline copy of the bar
        Assert.Equal(1, bars);
        Assert.False(visibleBefore);
        Assert.True(visibleWithError);
        Assert.False(visibleAfterDismiss);
    }

    private static void Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    internal static Color Token(StyledElement element, string key)
    {
        Assert.True(element.TryFindResource(key, element.ActualThemeVariant, out var value), $"The theme has no {key}.");
        return (Color)value!;
    }

    /// <summary>The color that <paramref name="tint"/> gives when it is drawn over the opaque <paramref name="surface"/>.</summary>
    private static Color Over(Color tint, Color surface)
    {
        byte Mix(byte top, byte below) => (byte)Math.Round(below + (top - below) * tint.A / 255.0);
        return Color.FromRgb(Mix(tint.R, surface.R), Mix(tint.G, surface.G), Mix(tint.B, surface.B));
    }

    private static void AssertClose(Color expected, Color actual)
    {
        // the renderer rounds a blend a little differently than the formula does
        const int Tolerance = 2;
        Assert.True(
            Math.Abs(expected.R - actual.R) <= Tolerance && Math.Abs(expected.G - actual.G) <= Tolerance && Math.Abs(expected.B - actual.B) <= Tolerance,
            $"Expected {expected}, but the bar shows {actual}.");
    }

    /// <summary>The contrast ratio of two colors as WCAG 2 defines it.</summary>
    internal static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            var linear = value / 255.0;
            return linear <= 0.03928 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        var (lighter, darker) = (Math.Max(Luminance(first), Luminance(second)), Math.Min(Luminance(first), Luminance(second)));
        return (lighter + 0.05) / (darker + 0.05);
    }

    internal static Color Pixel(ILockedFramebuffer buffer, Point at)
    {
        // the headless frame buffer is Rgba8888, see HeadlessApp
        var offset = (int)at.Y * buffer.RowBytes + (int)at.X * 4;
        return Color.FromArgb(Marshal.ReadByte(buffer.Address, offset + 3), Marshal.ReadByte(buffer.Address, offset), Marshal.ReadByte(buffer.Address, offset + 1), Marshal.ReadByte(buffer.Address, offset + 2));
    }
}
