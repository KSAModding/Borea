using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views.Pages;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class AddingToLineTests
{
    private sealed record Line(bool Visible, string? Name, string? ToolTip, string Text, int TextLines, bool Trimmed, double? TabsTop, bool KeepsTheSpace);

    private static async Task<T> RenderAsync<T>(MainViewModel viewModel, Func<Control> createPage, double width, Func<Control, Task<T>> read)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await HeadlessApp.Session.Dispatch(async () =>
        {
            var page = createPage();
            var window = new Window { Width = width, Height = 600, DataContext = viewModel, Content = page };
            window.Show();
            try
            {
                return await read(page);
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token);
    }

    private static Button LineButton(MainViewModel viewModel, Control page)
        => page.GetVisualDescendants().OfType<Button>().Single(button => button.Command == viewModel.SetMainWindowLibraryCommand);

    private static Line ReadLine(MainViewModel viewModel, Control page)
    {
        page.UpdateLayout();
        var button = LineButton(viewModel, page);
        var parts = ((Grid)button.Content!).Children.OfType<TextBlock>().ToList();
        var (before, name) = (parts[0], parts[1]);
        var modsTab = page.GetVisualDescendants().OfType<Button>().FirstOrDefault(tab => tab.Command == viewModel.ShowDiscoverModsCommand);
        return new Line(
            button.IsEffectivelyVisible,
            AutomationProperties.GetName(button),
            ToolTip.GetTip(button) as string,
            string.Concat(parts.Select(part => part.Text)),
            parts.Max(part => part.TextLayout.TextLines.Count),
            name.TextLayout.TextLines.Any(line => line.HasCollapsed),
            modsTab?.TranslatePoint(default, page)?.Y,
            name.Bounds.X >= before.TextLayout.WidthIncludingTrailingWhitespace - 0.5 && before.TextLayout.WidthIncludingTrailingWhitespace > before.TextLayout.Width);
    }

    private static List<(string? ToolTip, string? Name)> AddButtons(Control page, IEnumerable<ICommand> commands)
    {
        page.UpdateLayout();
        var wanted = commands.ToHashSet();
        return page.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.Command is { } command && wanted.Contains(command))
            .Select(button => (ToolTip.GetTip(button) as string, AutomationProperties.GetName(button)))
            .ToList();
    }

    private static void Click(MainViewModel viewModel, Control page)
    {
        var button = LineButton(viewModel, page);
        var window = (Window)TopLevel.GetTopLevel(page)!;
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }

    private static async Task<ViewModelHarness> CreateAsync(string name, Func<string, string>? editSnapshot = null)
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: editSnapshot);
        await harness.Services.Instances.CreateAsync(name, InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        harness.ViewModel.SetMainWindowDiscoverCommand.Execute(null);
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return harness;
    }

    private static void AssertNamesAlpha(Line line)
    {
        Assert.True(line.Visible);
        Assert.Equal("Adding to Alpha", line.Text);
        Assert.Equal("Adding to Alpha", line.Name);
        Assert.Equal("Adding to Alpha", line.ToolTip);
        Assert.Equal(1, line.TextLines);
        Assert.False(line.Trimmed);
        Assert.True(line.KeepsTheSpace);
    }

    [Fact]
    public async Task Discover_ShowsTheLineOnActivationWithoutMovingTheListAndAClickOpensTheLibrary()
    {
        using var harness = await CreateAsync("Alpha");
        var viewModel = harness.ViewModel;
        await viewModel.ActiveInstance!.ToggleActiveCommand.ExecuteAsync(null);

        var (inactive, active, adds, openedLibrary) = await RenderAsync(viewModel, () => new DiscoverPage(), 1200, async page =>
        {
            var inactive = ReadLine(viewModel, page);
            await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
            var active = ReadLine(viewModel, page);
            var adds = AddButtons(page, viewModel.DiscoverItems.Select(item => item.InstallCommand));
            Click(viewModel, page);
            return (inactive, active, adds, viewModel.CurrentWindowLibrary);
        });

        Assert.False(inactive.Visible);
        AssertNamesAlpha(active);
        Assert.NotNull(active.TabsTop);
        Assert.Equal(inactive.TabsTop, active.TabsTop);
        Assert.NotEmpty(adds);
        Assert.All(adds, add => Assert.Equal(("Add to Alpha", "Add to Alpha"), add));
        Assert.True(openedLibrary);
    }

    [Fact]
    public async Task Discover_FollowsARenameAndALanguageSwitch()
    {
        using var harness = await CreateAsync("Alpha");
        var viewModel = harness.ViewModel;

        var (renamed, german, germanAdds) = await RenderAsync(viewModel, () => new DiscoverPage(), 1200, async page =>
        {
            await viewModel.RenameInstanceAsync(viewModel.ActiveInstance!, "Game profile");
            var renamed = ReadLine(viewModel, page);
            harness.Localization.TrySetCulture("de");
            return (renamed, ReadLine(viewModel, page), AddButtons(page, viewModel.DiscoverItems.Select(item => item.InstallCommand)));
        });

        Assert.Equal("Adding to Game profile", renamed.Text);
        Assert.Equal("Adding to Game profile", renamed.ToolTip);
        Assert.Equal(harness.Localization.FormatDiscoverAddingTo("Game profile"), german.Text);
        Assert.NotEqual(renamed.Text, german.Text);
        Assert.NotEmpty(germanAdds);
        Assert.All(germanAdds, add => Assert.Equal(harness.Localization.FormatDiscoverAddTo("Game profile"), add.ToolTip));
    }

    [Fact]
    public async Task Discover_LongName_IsCutInOneLineWithTheFullNameInTheToolTip()
    {
        var name = string.Concat(Enumerable.Repeat("Very long instance name ", 8)).Trim();
        using var harness = await CreateAsync(name);

        var line = await RenderAsync(harness.ViewModel, () => new DiscoverPage(), 700, page => Task.FromResult(ReadLine(harness.ViewModel, page)));

        Assert.True(line.Visible);
        Assert.True(line.Trimmed);
        Assert.Equal(1, line.TextLines);
        Assert.Equal(harness.Localization.FormatDiscoverAddingTo(name), line.ToolTip);
    }

    [Fact]
    public async Task Discover_NoInstance_HidesTheLine()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        var line = await RenderAsync(harness.ViewModel, () => new DiscoverPage(), 1200, page => Task.FromResult(ReadLine(harness.ViewModel, page)));

        Assert.False(line.Visible);
    }

    [Fact]
    public async Task ModpacksTab_AddButtonsNameTheInstance()
    {
        using var harness = await CreateAsync("Alpha", InstanceViewModelTests.WithPack("starter-pack", "Starter Pack", "1.0.0", ("AdvancedFlightComputer", "0.7.5")));
        var viewModel = harness.ViewModel;
        viewModel.ShowDiscoverModpacksCommand.Execute(null);

        var adds = await RenderAsync(viewModel, () => new DiscoverPage(), 1200, page => Task.FromResult(AddButtons(page, viewModel.DiscoverPacks.Select(pack => pack.InstallCommand))));

        Assert.Equal(("Add to Alpha", "Add to Alpha"), Assert.Single(adds));
    }

    [Fact]
    public async Task ModPage_NamesTheInstanceOnTheLineTheAddButtonAndTheVersionRows()
    {
        using var harness = await CreateAsync("Alpha");
        var viewModel = harness.ViewModel;
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        var (line, add, versions) = await RenderAsync(viewModel, () => new Borea.App.Views.Pages.ContentPage(), 1200, page => Task.FromResult((
            ReadLine(viewModel, page),
            AddButtons(page, [viewModel.SelectedContent!.InstallCommand]),
            AddButtons(page, viewModel.ContentVersions.Select(version => version.InstallCommand)))));

        AssertNamesAlpha(line);
        Assert.Equal(("Add to Alpha", "Add to Alpha"), Assert.Single(add));
        Assert.Equal(viewModel.ContentVersions.Count, versions.Count);
        Assert.All(versions, version => Assert.Equal(("Add to Alpha", "Add to Alpha"), version));
    }

    [Fact]
    public async Task PackPage_NamesTheInstanceOnTheLineTheAddButtonAndTheVersionRows()
    {
        using var harness = await CreateAsync("Alpha", InstanceViewModelTests.WithPack("starter-pack", "Starter Pack", "1.0.0", ("AdvancedFlightComputer", "0.7.5")));
        var viewModel = harness.ViewModel;
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackVersionsCommand.Execute(null);

        var (line, add, versions) = await RenderAsync(viewModel, () => new PackPage(), 1200, page => Task.FromResult((
            ReadLine(viewModel, page),
            AddButtons(page, [viewModel.SelectedPack!.InstallCommand]),
            AddButtons(page, viewModel.PackVersions.Select(version => version.InstallCommand)))));

        AssertNamesAlpha(line);
        Assert.Equal(("Add to Alpha", "Add to Alpha"), Assert.Single(add));
        Assert.Equal(viewModel.PackVersions.Count, versions.Count);
        Assert.All(versions, version => Assert.Equal(("Add to Alpha", "Add to Alpha"), version));
    }
}
