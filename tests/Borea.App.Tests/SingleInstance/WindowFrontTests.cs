using Avalonia.Controls;
using Borea.App.SingleInstance;
using Borea.App.Tests.Views;

namespace Borea.App.Tests.SingleInstance;

[Collection(HeadlessCollection.Name)]
public sealed class WindowFrontTests
{
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public async Task BringToFront_RestoresTheStateBeforeTheWindowWasMinimized(WindowState before)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var restored = await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window();
            var front = new WindowFront(window);
            window.Show();
            window.WindowState = before;
            window.WindowState = WindowState.Minimized;

            front.BringToFront();

            var state = window.WindowState;
            window.Close();
            return state;
        }, timeout.Token);

        Assert.Equal(before, restored);
    }

    [Fact]
    public async Task BringToFront_KeepsAWindowThatIsNotMinimized()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var state = await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { WindowState = WindowState.Maximized };
            var front = new WindowFront(window);
            window.Show();

            front.BringToFront();

            var current = window.WindowState;
            window.Close();
            return current;
        }, timeout.Token);

        Assert.Equal(WindowState.Maximized, state);
    }
}
