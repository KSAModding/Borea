using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
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
}
