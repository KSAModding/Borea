using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ToastHostTests
{
    [Fact]
    public async Task InstanceHintToast_ShowsOnlyItsAction_AndAClickOpensTheLibrary()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        viewModel.ShowInstanceHintToast();

        var actions = await HeadlessApp.RunAsync(harness, () =>
        {
            var host = new ToastHost();
            var window = new Window { Width = 800, Height = 600, DataContext = viewModel, Content = host };
            window.Show();
            try
            {
                host.UpdateLayout();
                // the click finds the button only in a rendered frame
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var buttons = host.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible && button.Content is string)
                    .ToList();
                var labels = buttons.Select(button => (string)button.Content!).ToList();
                var open = buttons.Single(button => (string)button.Content! == harness.Localization.DiscoverOpenLibrary);
                var point = open.TranslatePoint(new Point(open.Bounds.Width / 2, open.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                return Task.FromResult(labels);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal([harness.Localization.DiscoverOpenLibrary], actions);
        Assert.True(viewModel.CurrentWindowLibrary);
        Assert.Empty(viewModel.Toasts.Items);
    }
}
