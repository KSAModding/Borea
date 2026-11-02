using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class DiscoverRowTests
{
    [Theory]
    [InlineData(860)]
    [InlineData(1000)]
    [InlineData(1280)]
    public async Task RemoveConfirmation_KeepsItsTextInsideTheRow(double windowWidth)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        var item = harness.ViewModel.DiscoverItems.First();
        item.IsInstalled = true;
        item.IsConfirmingRemove = true;

        var layout = await ConfirmationAsync(harness, windowWidth);

        Assert.Equal([], layout.Outside);
        Assert.False(layout.Trimmed);
        Assert.Equal(1, layout.QuestionLines);
        Assert.Equal(layout.PanelRight, layout.ButtonsRight, 1);
    }

    [Fact]
    public async Task AuthorThatDoesNotFit_DropsOutWithItsComma_AndTheTooltipNamesEveryAuthor()
    {
        const string first = "Felix Kater";
        const string second = "Dot Ramirez with a very long name that takes the whole line";
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot => snapshot.Replace("\"Laurens\"", $"\"{first}\", \"{second}\"", StringComparison.Ordinal));
        Assert.True(harness.Localization.TrySetCulture("de"));
        await harness.ViewModel.EnsureDiscoverLoadedAsync();

        var (shown, hint) = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new DiscoverPage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = 860, Height = 832, Content = body, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            var links = page.GetVisualDescendants().OfType<Button>().Where(button => button.CommandParameter is AuthorLink { Name: first or second }).ToList();
            var line = links[0].GetVisualAncestors().OfType<ItemsControl>().First();
            var inLine = links.Where(link => link.TranslatePoint(new Point(0, 0), line)!.Value.Y < line.Bounds.Height);
            var result = (inLine.Select(link => string.Concat(link.GetVisualDescendants().OfType<TextBlock>().Single().Inlines!.OfType<Run>().Select(run => run.Text))).ToList(), ToolTip.GetTip(links[0]));

            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal([first], shown);
        Assert.Equal(harness.Localization.FormatDiscoverAuthorLink(first) + $"\n{first}, {second}", hint);
    }

    /// <summary>
    /// Renders Discover next to a navigation rail, as the main window does, and
    /// measures how the remove confirmation sits in its row.
    /// </summary>
    private static Task<ConfirmationLayout> ConfirmationAsync(ViewModelHarness harness, double windowWidth) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new DiscoverPage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = 832, Content = body, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            var row = page.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
            var question = row.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == harness.Localization.ContentRemoveConfirm);
            var confirmation = question.GetVisualAncestors().OfType<Panel>().First();
            var buttons = confirmation.GetVisualChildren().OfType<StackPanel>().First();
            var outside = confirmation.GetVisualDescendants()
                .OfType<Control>()
                .Where(control => control is TextBlock or Button)
                .Where(control => !Fits(control, row))
                .Select(control => control.ToString() ?? string.Empty)
                .ToList();
            var trimmed = question.TextLayout.TextLines.Any(line => line.HasCollapsed);
            var result = new ConfirmationLayout(outside, trimmed, question.TextLayout.TextLines.Count, RightEdge(buttons, row), RightEdge(confirmation, row));

            window.Close();
            return Task.FromResult(result);
        });

    private static bool Fits(Control control, Border row)
    {
        var corner = Corner(control, row);

        return corner.X >= row.Padding.Left
            && corner.Y >= row.Padding.Top
            && corner.X + control.Bounds.Width <= row.Bounds.Width - row.Padding.Right
            && corner.Y + control.Bounds.Height <= row.Bounds.Height - row.Padding.Bottom;
    }

    private static double RightEdge(Control control, Border row) => Corner(control, row).X + control.Bounds.Width;

    /// <summary>
    /// Gives the top left corner of the control in the coordinates of the row, and fails
    /// when the control has no place in the row, because an unplaced control would
    /// otherwise pass every measurement in this file.
    /// </summary>
    private static Point Corner(Control control, Border row) =>
        control.TranslatePoint(new Point(0, 0), row)
        ?? throw new InvalidOperationException($"{control} is not laid out inside the row.");

    private sealed record ConfirmationLayout(List<string> Outside, bool Trimmed, int QuestionLines, double ButtonsRight, double PanelRight);
}
