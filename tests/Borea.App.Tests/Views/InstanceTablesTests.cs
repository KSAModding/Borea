using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

/// <summary>
/// The tabs of the instance page show their rows and their empty text in a
/// bordered table with a header, and the table keeps its action inside it at
/// every window width of the design.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class InstanceTablesTests
{
    private static Task<T> OnInstancePageAsync<T>(ViewModelHarness harness, double width, Func<Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = width, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });

    private static async Task<ViewModelHarness> EmptyInstanceAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        return harness;
    }

    /// <summary>The bordered table around the visible text, or null when the text stands on its own.</summary>
    private static Border? TableAround(Control page, string text)
        => page.GetVisualDescendants().OfType<TextBlock>()
            .Single(block => block.IsEffectivelyVisible && block.Text == text)
            .GetVisualAncestors().OfType<Border>()
            .FirstOrDefault(border => border.BorderThickness == new Thickness(1) && border.CornerRadius == new CornerRadius(16));

    private static List<string> TextsIn(Visual table)
        => table.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Text is not null)
            .Select(block => block.Text!)
            .ToList();

    /// <summary>The visible button that shows the label, as its content or as the text inside it.</summary>
    private static Button ButtonIn(Visual table, string label)
        => table.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && (button.Content as string == label || button.Content is TextBlock { Text: var text } && text == label));

    private static bool Holds(Visual table, Visual inner)
    {
        var corner = inner.TranslatePoint(default, table)!.Value;
        return corner.X >= 0 && corner.X + inner.Bounds.Width <= table.Bounds.Width;
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task NoContent_ShowsTheEmptyTextInTheModsTableWithTheWayToDiscover(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;

        var (texts, command, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var table = TableAround(page, localization.InstanceEmptyContent)!;
            var discover = ButtonIn(table, localization.HomeDiscoverMods);
            return (TextsIn(table), discover.Command, Holds(table, discover));
        });

        Assert.Contains(localization.InstanceGroupMods, texts);
        Assert.Same(viewModel.SetMainWindowDiscoverCommand, command);
        Assert.True(fits);
    }

    [Fact]
    public async Task NoContent_OnAnInactiveInstance_OffersNoWayToDiscover()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(second.InstanceId);
        await viewModel.LoadAsync();
        var main = viewModel.Instances.Single(instance => instance.Name == "Main");
        Assert.False(main.IsActive);
        await main.OpenCommand.ExecuteAsync(null);

        var buttons = await OnInstancePageAsync(harness, 1280, page =>
            TableAround(page, localization.InstanceEmptyContent)!.GetVisualDescendants().OfType<Button>().Count(button => button.IsEffectivelyVisible));

        Assert.Equal(0, buttons);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task NoManualInstalls_ShowsTheEmptyTextInATableThatOpensTheModsFolder(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);

        var (texts, command, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var table = TableAround(page, localization.ManualInstallsEmpty)!;
            var open = ButtonIn(table, localization.GameDataOpenFolder);
            return (TextsIn(table), open.Command, Holds(table, open));
        });

        Assert.Contains(localization.InstanceTabManualInstalls, texts);
        Assert.True(fits);

        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        command!.Execute(null);

        Assert.Equal([harness.Services.Paths.GetInstanceModsFolder(viewModel.SelectedInstance!.InstanceId)], opened);
        Assert.Empty(viewModel.Toasts.Items);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task ManualInstalls_TheHeaderExplainsThatTheGameLoadsTheseMods(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        var folder = Path.Combine(harness.Services.Paths.GetInstanceModsFolder(viewModel.SelectedInstance!.InstanceId), "LocalOnly");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), "name = \"LocalOnly\"");
        await viewModel.ShowInstanceManualInstallsCommand.ExecuteAsync(null);

        var (info, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var table = TableAround(page, "LocalOnly")!;
            var info = table.GetVisualDescendants().OfType<InfoButton>().Single(button => button.IsEffectivelyVisible);
            return (info.Text, Holds(table, info));
        });

        Assert.Equal(localization.ManualInstallsInfo, info);
        Assert.True(fits);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task NoLog_ShowsTheEmptyTextInATableWithThePathAndReload(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await viewModel.ShowInstanceLogCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsGameLogMissing);

        var (texts, command, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var table = TableAround(page, localization.GameLogMissing)!;
            var reload = ButtonIn(table, localization.GameLogReload);
            return (TextsIn(table), reload.Command, Holds(table, reload));
        });

        Assert.Contains(viewModel.GameLogPathText!, texts);
        Assert.Same(viewModel.ReloadGameLogCommand, command);
        Assert.True(fits);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task GameData_RowsShareOneTableWithAHeader(double width)
    {
        using var harness = await EmptyInstanceAsync();
        var viewModel = harness.ViewModel;
        var localization = harness.Localization;
        await viewModel.ShowInstanceGameDataCommand.ExecuteAsync(null);
        var names = viewModel.GameDataItems.Select(item => item.Name).ToList();
        Assert.NotEmpty(names);

        var (tables, texts, fits) = await OnInstancePageAsync(harness, width, page =>
        {
            var tables = names.Select(name => TableAround(page, name)).Distinct().ToList();
            var table = tables[0]!;
            var buttons = table.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
            return (tables, TextsIn(table), buttons.Count == names.Count && buttons.All(button => Holds(table, button)));
        });

        Assert.NotNull(Assert.Single(tables));
        Assert.Contains(localization.InstanceTabGameData, texts);
        Assert.True(fits);
    }
}
