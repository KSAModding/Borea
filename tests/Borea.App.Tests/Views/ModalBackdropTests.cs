using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ModalBackdropTests
{
    [Fact]
    public async Task PressOnTheBackdrop_ClosesTheModal_ButNotInsideItOrWhileTheCloseButtonIsOff()
    {
        var closes = await HeadlessApp.RunAsync(() =>
        {
            var count = 0;
            var close = new Button { Command = new RelayCommand(() => count++) };
            var card = new Border { Width = 100, Height = 100, Background = Brushes.Gray, Child = close };
            var backdrop = new Panel { Background = Brushes.Black, Children = { card } };
            ModalBackdrop.SetCloseButton(backdrop, close);
            var window = new Window { Width = 400, Height = 300, Content = backdrop };
            window.Show();
            window.UpdateLayout();

            Press(window, card.TranslatePoint(new Point(90, 90), window)!.Value);
            var afterInside = count;
            Press(window, new Point(10, 10));
            var afterBackdrop = count;
            close.IsEnabled = false;
            Press(window, new Point(10, 10));
            var result = (afterInside, afterBackdrop, AfterOff: count);
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal((0, 1, 1), closes);
    }

    [Fact]
    public async Task PressBesideTheNameModal_CancelsIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.BeginCreateInstanceCommand.Execute(null);

        var open = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new InstanceNameModal();
            var window = new Window { Width = 1280, Height = 832, DataContext = viewModel, Content = modal };
            window.Show();
            window.UpdateLayout();
            var card = modal.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("modal"));

            Press(window, card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height - 8), window)!.Value);
            var afterInside = viewModel.IsNameModalOpen;
            Press(window, new Point(10, 10));
            var result = (afterInside, AfterBackdrop: viewModel.IsNameModalOpen);
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal((true, false), open);
    }

    [Fact]
    public async Task PressBesideTheGitHubSignIn_KeepsItRunning()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.IsGitHubSignInOpen = true;

        var open = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, DataContext = viewModel, Content = new GitHubSignInModal() };
            window.Show();
            window.UpdateLayout();
            Press(window, new Point(10, 10));
            var result = viewModel.IsGitHubSignInOpen;
            window.Close();
            return Task.FromResult(result);
        });

        Assert.True(open);
    }

    private static void Press(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
