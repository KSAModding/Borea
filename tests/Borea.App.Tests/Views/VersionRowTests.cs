using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class VersionRowTests
{
    private sealed record Row(string Version, double? FirstButtonX, double? AddButtonX);

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task ModPage_InstalledVersionHasNoAddButtonAndTheChangelogButtonsStayInOneColumn(double width)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, version: "0.7.3");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        var rows = await RenderAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), width, page => viewModel.ContentVersions
            .Select(version => new Row(version.Version, X(page, version.ToggleChangelogCommand), X(page, version.InstallCommand)))
            .ToList());

        AssertAligned(rows, installed: "0.7.3");
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_InstalledVersionHasNoAddButtonAndTheNewInstanceButtonsStayInOneColumn(double width)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "starter-pack",
            "Starter Pack",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.4")),
            PackViewModelTests.Version("1.1.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.5")))));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackVersionsCommand.Execute(null);

        var rows = await RenderAsync(harness, () => new PackPage(), width, page => viewModel.PackVersions
            .Select(version => new Row(version.Version, X(page, version.NewInstanceCommand), X(page, version.InstallCommand)))
            .ToList());

        AssertAligned(rows, installed: "1.0.0");
    }

    private static void AssertAligned(List<Row> rows, string installed)
    {
        Assert.True(rows.Count > 1);
        Assert.Null(rows.Single(row => row.Version == installed).AddButtonX);
        var others = rows.Where(row => row.Version != installed).ToList();
        Assert.All(others, row => Assert.NotNull(row.AddButtonX));
        Assert.Single(others.Select(row => row.AddButtonX).Distinct());
        Assert.All(rows, row => Assert.NotNull(row.FirstButtonX));
        Assert.Single(rows.Select(row => row.FirstButtonX).Distinct());
    }

    /// <summary>The left edge of the visible button that runs <paramref name="command"/>, or null when none shows.</summary>
    private static double? X(Control page, ICommand command) => page.GetVisualDescendants().OfType<Button>()
        .Where(button => button.IsEffectivelyVisible && button.Command == command)
        .Select(button => (double?)button.TranslatePoint(default, page)!.Value.X)
        .SingleOrDefault();

    private static Task<T> RenderAsync<T>(ViewModelHarness harness, Func<Control> createPage, double width, Func<Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = createPage();
            var window = new Window { Width = width, Height = 800, DataContext = harness.ViewModel, Content = page };
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
}
