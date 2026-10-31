using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Game;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class InstanceMenuTests
{
    [Fact]
    public async Task LibraryHomeAndInstancePage_ShareTheMenuOfTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().ActivateCommand.ExecuteAsync(null);
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        var id = viewModel.ActiveInstance!.InstanceId;

        var menus = await HeadlessApp.RunAsync(harness, () =>
            Task.FromResult(new Control[] { new LibraryPage(), new HomePage(), new InstancePage() }.Select(page =>
            {
                var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = viewModel };
                window.Show();
                window.UpdateLayout();
                var menu = page.GetVisualDescendants().OfType<InstanceMenu>().Single(control => control.IsEffectivelyVisible);
                window.Close();
                return (menu.DataContext as InstanceItem)?.InstanceId;
            }).ToList()));

        Assert.All(menus, menu => Assert.Equal(id, menu));
    }

    [Fact]
    public async Task KeepRefusedForTheGameFolder_DisablesTheKeepItemAndShowsTheReason()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        viewModel.GameFolderKeepRefusal = GameBuildKeepRefusal.WineWrapper;

        var (enabled, tip, texts) = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new LibraryPage();
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            var button = page.GetVisualDescendants().OfType<InstanceMenu>().Single(control => control.IsEffectivelyVisible)
                .GetVisualDescendants().OfType<Button>().First();
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            window.UpdateLayout();

            // the menu lives in a layer of its own above the page, so its entries are looked up from the window
            var shown = window.GetVisualDescendants().OfType<MenuItem>().Where(entry => entry.IsEffectivelyVisible).ToList();
            var keep = shown.Single(entry => Equals(entry.Header, viewModel.Localization.InstanceKeepGameBuild));
            var result = (keep.IsEffectivelyEnabled, ToolTip.GetTip(keep), shown.Select(entry => (entry.Header as TextBlock)?.Text).ToList());
            window.Close();
            return Task.FromResult(result);
        });

        Assert.False(enabled);
        Assert.Equal(viewModel.Localization.KeepGameBuildWineWrapper, tip);
        Assert.Contains(viewModel.Localization.KeepGameBuildWineWrapper, texts);
    }
}
