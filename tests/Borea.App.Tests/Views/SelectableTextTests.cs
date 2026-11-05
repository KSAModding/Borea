using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

/// <summary>
/// Issue #1163: a player can select and copy the texts that hold content, such as the name, the summary, the version and the
/// license of a mod or pack, and an error, while a selectable text keeps the look of the text classes.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SelectableTextTests
{
    private const string ModId = "AdvancedFlightComputer";

    private const string InstallError = "The archive of AdvancedFlightComputer 0.7.4 could not be read.";

    /// <summary>An active instance that holds another mod, so the content page of <see cref="ModId"/> opens as a player sees it.</summary>
    private static async Task<ViewModelHarness> ContentPageAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        await harness.ViewModel.DiscoverItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);
        harness.ViewModel.SelectedContent!.InstallError = InstallError;
        return harness;
    }

    private static async Task<ViewModelHarness> PackPageAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "flight-planning-essentials",
            "Flight Planning Essentials",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin(ModId, "0.7.4"), PackViewModelTests.Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);
        viewModel.SelectedPack!.InstallError = InstallError;
        return harness;
    }

    /// <summary>Shows the content page, or the pack page when <paramref name="pack"/> is set, and runs <paramref name="probe"/> while the window is open.</summary>
    private static Task<T> ShowAsync<T>(ViewModelHarness harness, bool pack, Func<Window, Control, Task<T>> probe) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            Control page = pack ? new Borea.App.Views.Pages.PackPage() : new Borea.App.Views.Pages.ContentPage();
            var window = new Window { Width = 1280, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                return await probe(window, page);
            }
            finally
            {
                window.Close();
            }
        });

    private static TextBlock Shown(Control page, string text) =>
        Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == text);

    [Fact]
    public async Task ContentPage_TheNameTheSummaryTheVersionTheLicenseAndAnError_AreSelectable()
    {
        using var harness = await ContentPageAsync();
        var content = harness.ViewModel.SelectedContent!;
        string[] texts = [content.Name, content.Abstract!, harness.ViewModel.LatestVersion!.Version, content.License!, InstallError];

        var kinds = await ShowAsync(harness, pack: false, (_, page) =>
            Task.FromResult(texts.Select(text => Shown(page, text).GetType()).ToList()));

        Assert.All(kinds, kind => Assert.Equal(typeof(SelectableTextBlock), kind));
    }

    [Fact]
    public async Task PackPage_TheNameTheSummaryTheVersionTheLicenseAndAnError_AreSelectable()
    {
        using var harness = await PackPageAsync();
        var pack = harness.ViewModel.SelectedPack!;
        string[] texts = [pack.Name, pack.Abstract!, pack.Version, pack.License!, InstallError];

        var kinds = await ShowAsync(harness, pack: true, (_, page) =>
            Task.FromResult(texts.Select(text => Shown(page, text).GetType()).ToList()));

        Assert.All(kinds, kind => Assert.Equal(typeof(SelectableTextBlock), kind));
    }

    [Fact]
    public async Task ContentPage_ADragOverAnErrorAndCopy_PutTheErrorOnTheClipboard()
    {
        using var harness = await ContentPageAsync();

        var copied = await ShowAsync(harness, pack: false, async (window, page) =>
        {
            var error = Shown(page, InstallError);
            var end = error.TextLayout.HitTestTextPosition(InstallError.Length);
            window.MouseDown(error.TranslatePoint(new Point(1, end.Center.Y), window)!.Value, MouseButton.Left);
            window.MouseMove(error.TranslatePoint(new Point(end.X + 4, end.Center.Y), window)!.Value, RawInputModifiers.LeftMouseButton);
            window.MouseUp(error.TranslatePoint(new Point(end.X + 4, end.Center.Y), window)!.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var copy = Application.Current!.PlatformSettings!.HotkeyConfiguration.Copy[0];
            window.KeyPress(copy.Key, (RawInputModifiers)copy.KeyModifiers, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            return await window.Clipboard!.TryGetTextAsync();
        });

        Assert.Equal(InstallError, copied);
    }

    /// <summary>The text classes that the views put on a selectable text, alone and in the pairs the views use.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("body-md")]
    [InlineData("body-md danger")]
    [InlineData("body-sm danger")]
    [InlineData("body-md secondary")]
    [InlineData("body-md muted")]
    [InlineData("label-md")]
    [InlineData("heading-lg")]
    [InlineData("heading-md")]
    public async Task TextClasses_GiveASelectableTextTheLookOfATextBlock(string classes)
    {
        var looks = await HeadlessApp.RunAsync(() =>
        {
            var plain = new TextBlock { Text = "AdvancedFlightComputer" };
            var selectable = new SelectableTextBlock { Text = "AdvancedFlightComputer" };
            plain.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            selectable.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Children = { plain, selectable } } };
            window.Show();
            try
            {
                window.UpdateLayout();
                static (Color?, double, string, FontWeight) Look(TextBlock text) =>
                    ((text.Foreground as ISolidColorBrush)?.Color, text.FontSize, text.FontFamily.ToString(), text.FontWeight);
                return Task.FromResult((Plain: Look(plain), Selectable: Look(selectable)));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(looks.Plain, looks.Selectable);
    }
}
