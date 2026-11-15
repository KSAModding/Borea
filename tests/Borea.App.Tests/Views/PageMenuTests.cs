using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.Core.Mods;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.Tests.Views;

/// <summary>Copy share link and Report in the menu of the mod page and the pack page, next to the copy button of the side panel.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class PageMenuTests
{
    private const string ModId = "AdvancedFlightComputer";

    private const string PackId = "armory-pack";

    private static void ClickCenter(Window window, Visual target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        window.UpdateLayout();
    }

    private static Button MoreMenu(ViewModelHarness harness, Control page) =>
        page.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Flyout is not null && AutomationProperties.GetName(button) == harness.Localization.LibraryMoreActions);

    /// <summary>The menu lives in a layer of its own above the page, so its entries are looked up from the window.</summary>
    private static List<MenuItem> ShownEntries(Window window)
        => window.GetVisualDescendants().OfType<MenuItem>().Where(entry => entry.IsEffectivelyVisible).ToList();

    private static Task<T> OnPageAsync<T>(ViewModelHarness harness, bool pack, Func<Window, Control, Task<T>> read) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            Control page = pack ? new Borea.App.Views.Pages.PackPage() : new Borea.App.Views.Pages.ContentPage();
            var window = new Window { Width = 1280, Height = 900, DataContext = harness.ViewModel, Content = page };
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

    /// <summary>
    /// Opens the menu and clicks the entry with the command, after opening the submenu with <paramref name="submenu"/> as header.
    /// Returns the header of the entry.
    /// </summary>
    private static async Task<object?> ClickEntryAsync(ViewModelHarness harness, Window window, Control page, ICommand command, object? parameter = null, string? submenu = null)
    {
        ClickCenter(window, MoreMenu(harness, page));
        if (submenu is not null)
            ClickCenter(window, ShownEntries(window).Single(entry => Equals(entry.Header, submenu)));

        var target = ShownEntries(window).Single(entry => ReferenceEquals(entry.Command, command) && Equals(entry.CommandParameter, parameter));
        var header = target.Header;
        ClickCenter(window, target);
        if (command is IAsyncRelayCommand { ExecutionTask: { } running })
            await running;

        return header;
    }

    private static async Task<ViewModelHarness> ModPageAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        await harness.ViewModel.DiscoverItems.Single(item => item.ModId == ModId).OpenCommand.ExecuteAsync(null);
        return harness;
    }

    private static async Task<ViewModelHarness> PackPageAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(
            PackViewModelTests.Pack(PackId, "Armory Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("KSArmory", "0.8.44")))));
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        harness.ViewModel.ShowDiscoverModpacksCommand.Execute(null);
        await harness.ViewModel.DiscoverPacks.Single().OpenCommand.ExecuteAsync(null);
        return harness;
    }

    [Theory]
    [InlineData(false, "https://ksamodding.github.io/Borea/mod/AdvancedFlightComputer/")]
    [InlineData(true, "https://ksamodding.github.io/Borea/pack/armory-pack/")]
    public async Task CopyShareLinkInTheMenu_CopiesTheSharePage(bool pack, string url)
    {
        using var harness = pack ? await PackPageAsync() : await ModPageAsync();
        var viewModel = harness.ViewModel;
        var clipboard = new UnexpectedErrorTests.FakeWindowServices();
        viewModel.WindowServices = clipboard;
        ICommand command = pack ? viewModel.CopyPackShareLinkCommand : viewModel.CopyContentShareLinkCommand;

        var header = await OnPageAsync(harness, pack, (window, page) => ClickEntryAsync(harness, window, page, command));

        Assert.Equal(harness.Localization.ContentCopyLink, header);
        Assert.Equal(url, clipboard.CopiedText);
        Assert.Equal(harness.Localization.ContentLinkCopied, viewModel.Toasts.Items[^1].Message);
    }

    [Theory]
    [InlineData(false, IndexReportKind.Takedown, "https://github.com/KSAModding/content-index/issues/new?template=takedown.yml&listing=AdvancedFlightComputer")]
    [InlineData(false, IndexReportKind.Dispute, "https://github.com/KSAModding/content-index/issues/new?template=id-dispute.yml&listing=AdvancedFlightComputer")]
    [InlineData(true, IndexReportKind.Takedown, "https://github.com/KSAModding/content-index/issues/new?template=takedown.yml&listing=armory-pack%201.0.0")]
    [InlineData(true, IndexReportKind.Dispute, "https://github.com/KSAModding/content-index/issues/new?template=id-dispute.yml&listing=armory-pack")]
    public async Task ReportInTheMenu_OpensTheFormWithTheIdFilledIn(bool pack, IndexReportKind kind, string url)
    {
        using var harness = pack ? await PackPageAsync() : await ModPageAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        ICommand command = pack ? viewModel.ReportPackCommand : viewModel.ReportContentCommand;

        await OnPageAsync(harness, pack, (window, page) => ClickEntryAsync(harness, window, page, command, kind, harness.Localization.ContentReport));

        Assert.Equal([url], opened);
        Assert.Null(pack ? viewModel.PackDetailError : viewModel.ContentDetailError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportSubmenu_SaysInOneLineWhatEachFormIsFor(bool pack)
    {
        using var harness = pack ? await PackPageAsync() : await ModPageAsync();
        var localization = harness.Localization;

        var lines = await OnPageAsync(harness, pack, (window, page) =>
        {
            ClickCenter(window, MoreMenu(harness, page));
            ClickCenter(window, ShownEntries(window).Single(entry => Equals(entry.Header, localization.ContentReport)));
            return Task.FromResult(ShownEntries(window)
                .Where(entry => entry.CommandParameter is IndexReportKind)
                .Select(entry => ((StackPanel)entry.Header!).Children.OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList())
                .ToList());
        });

        Assert.Equal(
            [
                [localization.ContentReportTakedown, localization.ContentReportTakedownHint],
                [localization.ContentReportDispute, localization.ContentReportDisputeHint],
            ],
            lines);
    }

    [Fact]
    public async Task ListingNotFromTheIndex_HasNeitherEntry()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.OpenContentCommand.ExecuteAsync(new DiscoverItem(viewModel, new ModMetadata(
            specVersion: 1, modId: "5000", source: "spacedock", name: "Old Mod", authors: ["Someone"], abstractText: "Old.", license: "MIT", links: new Dictionary<string, string> { ["forums"] = "https://forums.example/5000" }, gameMin: "2026.1.1.1")));

        var headers = await OnPageAsync(harness, false, (window, page) =>
        {
            ClickCenter(window, MoreMenu(harness, page));
            return Task.FromResult(ShownEntries(window).Select(entry => entry.Header).ToList());
        });

        Assert.Equal([harness.Localization.ContentAddFavorite], headers);
    }

    [Fact]
    public async Task ModPage_SidePanelKeepsTheCopyButtonUnderTheLinks()
    {
        using var harness = await ModPageAsync();
        var viewModel = harness.ViewModel;
        Assert.True(viewModel.HasContentLinks);

        var (copy, links) = await OnPageAsync(harness, false, (_, page) =>
        {
            var list = page.GetVisualDescendants().OfType<ItemsControl>().Single(control => ReferenceEquals(control.ItemsSource, viewModel.ContentLinks));
            var button = page.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.CopyContentShareLinkCommand));
            return Task.FromResult((button.IsEffectivelyVisible ? button.Bounds : default, list.Bounds));
        });

        Assert.Equal(links.Bottom + 10, copy.Top, 0.5);
    }

    [Fact]
    public async Task PackPage_SidePanelKeepsTheCopyButtonBetweenTheLinksAndTheForumList()
    {
        using var harness = await PackPageAsync();
        var viewModel = harness.ViewModel;

        var (links, copy, forumList) = await OnPageAsync(harness, true, (_, page) =>
        {
            var list = page.GetVisualDescendants().OfType<ItemsControl>().Single(control => ReferenceEquals(control.ItemsSource, viewModel.PackLinks));
            var button = page.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.CopyPackShareLinkCommand));
            var forum = page.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, viewModel.CopyPackForumListCommand));
            return Task.FromResult((list.Bounds, button.IsEffectivelyVisible ? button.Bounds : default, forum.IsEffectivelyVisible ? forum.Bounds : default));
        });

        Assert.Equal(links.Bottom + 10, copy.Top, 0.5);
        Assert.Equal(copy.Bottom + 10, forumList.Top, 0.5);
    }
}
