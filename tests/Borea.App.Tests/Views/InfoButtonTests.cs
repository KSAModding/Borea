using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class InfoButtonTests
{
    private const string Text = "The game profile is the folder KSA uses when you play without Borea.";

    [Fact]
    public async Task TabAndEnter_OpenTheFlyoutWithTheText()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var shown = await HeadlessApp.Session.Dispatch(() =>
        {
            var button = new InfoButton { Text = Text };
            var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { button } } };
            window.Show();
            window.UpdateLayout();

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            var focused = button.IsFocused;
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            var flyout = (Flyout)button.Flyout!;
            var result = (Focused: focused, flyout.IsOpen, FlyoutText: ((TextBlock)flyout.Content!).Text, Tip: ToolTip.GetTip(button), HelpText: AutomationProperties.GetHelpText(button));
            flyout.Hide();
            window.Close();
            return result;
        }, timeout.Token);

        Assert.True(shown.Focused);
        Assert.True(shown.IsOpen);
        Assert.Equal(Text, shown.FlyoutText);
        Assert.Equal(Text, shown.Tip);
        Assert.Equal(Text, shown.HelpText);
    }
}
