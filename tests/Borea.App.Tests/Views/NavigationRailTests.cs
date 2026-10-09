using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class NavigationRailTests
{
    /// <summary>
    /// How far below the other rail icons the weight of the house may sit.
    /// The lift leaves about 1.1 pixels of it, and without the lift it is about 2.2, which is the low icon the report is about.
    /// </summary>
    private const double Tolerance = 1.5;

    [Fact]
    public async Task TheHouseIcon_CarriesItsWeightLevelWithTheOtherRailIcons()
    {
        var session = HeadlessApp.Session;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var icons = await session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var measured = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nav"))
                .Select(button => (Lifted: button.Content is StyledElement icon && icon.Classes.Contains("optical-lift"), Center: IconCenter(button)))
                .ToArray();

            window.Close();
            return measured;
        }, timeout.Token);

        Assert.Equal(6, icons.Length);
        var house = icons.Single(icon => icon.Lifted).Center;
        var others = icons.Where(icon => !icon.Lifted).Average(icon => icon.Center);
        Assert.InRange(house - others, 0, Tolerance);
    }

    [Theory]
    [InlineData(false, "always", false)]
    [InlineData(true, "never", false)]
    [InlineData(true, "always", true)]
    public async Task StewardEntry_ShowsOnlyForASteward(bool signedIn, string bypass, bool shown)
    {
        var session = new StewardSession();
        session.Bypass["KSAModding/content-index"] = bypass;
        session.Bypass["KSAModding/content-index-releases"] = bypass;
        using var harness = await CreateAsync(session, signedIn);

        var (visible, tip, name) = await OnMainWindowAsync(harness, window =>
        {
            var button = window.FindControl<Button>("StewardButton")!;
            return Task.FromResult((button.IsEffectivelyVisible, ToolTip.GetTip(button), AutomationProperties.GetName(button)));
        });

        Assert.Equal(shown, harness.ViewModel.IsGitHubSteward);
        Assert.Equal(shown, visible);
        Assert.Equal(harness.Localization.NavigationSteward, tip);
        Assert.Equal(harness.Localization.NavigationSteward, name);
    }

    [Fact]
    public async Task StewardEntry_OpensTheStewardPage_AndIsActiveWhileItIsOpen()
    {
        using var harness = await CreateAsync(new StewardSession(), signedIn: true);
        var viewModel = harness.ViewModel;

        var (before, open, after) = await OnMainWindowAsync(harness, async window =>
        {
            var button = window.FindControl<Button>("StewardButton")!;
            var before = button.Classes.Contains("active");

            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await viewModel.StewardPage.Queue.WhenLoadedAsync();
            window.UpdateLayout();
            var open = (viewModel.CurrentWindowSteward, button.Classes.Contains("active"));

            viewModel.SetMainWindowHomeCommand.Execute(null);
            window.UpdateLayout();
            return (before, open, button.Classes.Contains("active"));
        });

        Assert.False(before);
        Assert.Equal((true, true), open);
        Assert.False(after);
        Assert.False(viewModel.CurrentWindowSteward);
    }

    [Fact]
    public async Task StewardEntry_AppearsAboveTasks_WithoutMovingAnyOtherEntry()
    {
        using var harness = await CreateAsync(new StewardSession(), signedIn: true);

        var (shown, hidden, stewardBottom, tasksTop) = await OnMainWindowAsync(harness, window =>
        {
            var steward = window.FindControl<Button>("StewardButton")!;
            var tasks = window.FindControl<Button>("TasksButton")!;
            Point TopOf(Control control) => control.TranslatePoint(default, window)!.Value;
            Point[] OtherEntries() => window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nav") && button != steward)
                .Select(TopOf)
                .ToArray();

            var shown = OtherEntries();
            var stewardBottom = TopOf(steward).Y + steward.Bounds.Height;
            var tasksTop = TopOf(tasks).Y;

            // the same rail for a player, without the entry
            steward.IsVisible = false;
            window.UpdateLayout();
            return Task.FromResult((shown, OtherEntries(), stewardBottom, tasksTop));
        });

        Assert.Equal(5, shown.Length);
        Assert.Equal(hidden, shown);
        Assert.Equal(tasksTop - 8, stewardBottom);
    }

    private static async Task<ViewModelHarness> CreateAsync(StewardSession session, bool signedIn)
    {
        if (signedIn)
            session.SignInDirectly();

        var harness = await ViewModelHarness.CreateAsync(gitHub: session, indexStatusEditor: new FakeIndexStatusEditor(), stewardQueue: new FakeStewardQueue());
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }

    private static Task<T> OnMainWindowAsync<T>(ViewModelHarness harness, Func<Window, Task<T>> body) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var window = new MainWindow { DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                return await body(window);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// Renders one rail button and returns the vertical center of the ink of its icon,
    /// with every pixel weighted by how much opacity the icon adds to the button below it.
    /// </summary>
    private static double IconCenter(Button button)
    {
        var width = (int)button.Bounds.Width;
        var height = (int)button.Bounds.Height;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(button);

        // a RenderTargetBitmap cannot be locked, so its pixels are read through a buffer of our own
        var stride = width * 4;
        var pixels = Marshal.AllocHGlobal(stride * height);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), pixels, stride * height, stride);

            // the third row holds the button alone, because no icon reaches that far up
            var background = Marshal.ReadByte(pixels, 2 * stride + width / 2 * 4 + 3);
            double weighted = 0;
            double ink = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    // only the icon makes a pixel more opaque than the flat background, the rounded corners make it less
                    var alpha = Math.Max(Marshal.ReadByte(pixels, y * stride + x * 4 + 3) - background, 0);
                    weighted += (y + 0.5) * alpha;
                    ink += alpha;
                }

            Assert.True(ink > 0, "the rail button paints no icon");
            return weighted / ink;
        }
        finally
        {
            Marshal.FreeHGlobal(pixels);
        }
    }
}
