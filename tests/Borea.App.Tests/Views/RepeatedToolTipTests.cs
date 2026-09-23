using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class RepeatedToolTipTests
{
    [Theory]
    [InlineData("0.8.0", "0.8.0", 400, false)]
    [InlineData("0.8.0-a-very-long-pre-release-name", "0.8.0-a-very-long-pre-release-name", 40, true)]
    [InlineData("0.8.0", "Released on 1.9.2026", 400, true)]
    public async Task ToolTip_OpensOnlyWhenItAddsSomething(string text, string tip, double width, bool opens)
    {
        var cancelled = await HeadlessApp.RunAsync(() =>
        {
            var block = new TextBlock { Text = text, Width = width, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(block, tip);
            var window = new Window { Width = 600, Height = 200, Content = block };
            window.Show();
            window.UpdateLayout();
            var opening = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent);
            block.RaiseEvent(opening);
            window.Close();
            return Task.FromResult(opening.Cancel);
        });

        Assert.Equal(!opens, cancelled);
    }
}
