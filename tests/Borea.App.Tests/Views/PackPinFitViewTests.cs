using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Game;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class PackPinFitViewTests
{
    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_NamesThePinAboveItsGameMaxInTheSidePanel_AndMarksItsRow(double windowWidth)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "flight-pack",
            "Flight Pack",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.3"), PackViewModelTests.Pin("KSArmory", "0.8.44")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.9.7.5402", out var installed));
        await viewModel.RefreshCompatibilityAsync(installed);
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await viewModel.DiscoverPacks.Single().OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackModsCommand.Execute(null);
        var fit = harness.Localization.FormatPackMemberAboveGameMax("AdvancedFlightComputer", "0.7.3", "2026.8.22.5348");

        var outside = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new PackPage { DataContext = viewModel };
            Grid.SetColumn(page, 1);
            var window = new Window
            {
                Width = windowWidth,
                Height = 832,
                DataContext = viewModel,
                Content = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } },
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                var body = page.GetVisualDescendants().OfType<PageBodyPanel>().Single().GetVisualChildren().OfType<StackPanel>().Single();
                var panel = page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("side-panel"));
                var chip = Assert.Single(Shown(body, harness.Localization.CompatibilityIncompatible));
                Assert.Equal(fit, ToolTip.GetTip(chip.GetVisualAncestors().OfType<Border>().First()));
                return Task.FromResult<List<string>>([.. Outside(panel, Assert.Single(Shown(panel, fit))), .. Outside(body, chip)]);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Empty(outside);
    }

    private static IEnumerable<TextBlock> Shown(Control container, string text)
        => container.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Text == text && block.IsEffectivelyVisible);

    private static IEnumerable<string> Outside(Control container, TextBlock text)
    {
        var corner = text.TranslatePoint(new Point(0, 0), container) ?? throw new InvalidOperationException($"{text.Text} is not laid out inside {container}.");
        var fits = corner.X >= 0 && corner.Y >= 0
            && corner.X + text.Bounds.Width <= container.Bounds.Width + 0.5
            && corner.Y + text.Bounds.Height <= container.Bounds.Height + 0.5
            && !text.TextLayout.TextLines.Any(line => line.HasCollapsed);
        return fits ? [] : [text.Text ?? string.Empty];
    }
}
