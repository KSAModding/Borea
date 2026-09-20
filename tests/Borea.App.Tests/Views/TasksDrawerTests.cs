using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.Core.History;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class TasksDrawerTests
{
    /// <summary>A name long enough that the title needs the second line the row offers.</summary>
    private const string LongSubject = "Advanced Flight Computer and Powered Guidance for Kittens 1.2.3";

    private sealed record Row(bool TitleTrimmed, int TitleLines, double TitleTop, double StateCenter, double TimeCenter);

    [Fact]
    public async Task HistoryRow_WrapsALongTitle_AndPutsTheTimeOnTheStateLine()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var task = viewModel.Tasks.Start(TaskKind.ModInstall, LongSubject, instanceId: null, instanceName: null, contentId: null, version: null, TaskState.Running);
        viewModel.Tasks.End(task, TaskState.Finished);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var row = await HeadlessApp.Session.Dispatch(() =>
        {
            var drawer = new TasksDrawer();
            var window = new Window { Width = 1280, Height = 832, DataContext = viewModel, Content = drawer };
            window.Show();
            try
            {
                drawer.UpdateLayout();
                var title = drawer.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == task.Title);
                var card = title.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("card"));
                TextBlock Block(string text) => card.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == text);
                double Top(TextBlock block) => block.TranslatePoint(default, card)!.Value.Y;
                double Center(TextBlock block) => block.TranslatePoint(new Point(0, block.Bounds.Height / 2), card)!.Value.Y;

                return Task.FromResult(new Row(
                    title.TextLayout.TextLines.Any(line => line.HasCollapsed),
                    title.TextLayout.TextLines.Count,
                    Top(title),
                    Center(Block(task.StateText)),
                    Center(Block(task.TimeText))));
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token);

        Assert.Equal(2, row.TitleLines);
        Assert.False(row.TitleTrimmed);
        Assert.True(row.TitleTop < row.StateCenter);
        Assert.Equal(row.StateCenter, row.TimeCenter, 1);
    }
}
