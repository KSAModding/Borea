using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class HomePageTests
{
    [Fact]
    public async Task DiscoverMods_TheTextKeepsRoomInsideTheHoverAndItsPlaceInTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var (left, right, textRight, rowRight, rowHeight, headingHeight) = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new HomePage { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var text = page.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == viewModel.Localization.HomeDiscoverMods);
                var link = text.GetVisualAncestors().OfType<Button>().First();
                var row = (Grid)link.Parent!;
                var heading = row.Children.OfType<TextBlock>().Single();
                var start = Corner(text, link).X;
                return Task.FromResult((
                    start,
                    link.Bounds.Width - start - text.Bounds.Width,
                    Corner(text, page).X + text.Bounds.Width,
                    Corner(row, page).X + row.Bounds.Width,
                    row.Bounds.Height,
                    heading.Bounds.Height));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(left >= 8, $"The text starts {left} px inside the link.");
        Assert.True(right >= 8, $"The text ends {right} px inside the link.");
        Assert.Equal(rowRight, textRight, 0.5);
        Assert.Equal(headingHeight, rowHeight, 0.5);
    }

    private static Point Corner(Visual control, Visual page) =>
        control.TranslatePoint(new Point(0, 0), page)
        ?? throw new InvalidOperationException($"{control} is not laid out inside the page.");
}
