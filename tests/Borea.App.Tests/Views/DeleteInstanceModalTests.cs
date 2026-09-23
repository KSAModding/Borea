using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class DeleteInstanceModalTests
{
    [Fact]
    public async Task Delete_NamesTheInstanceAndDeletesIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);

        var texts = await HeadlessApp.RunAsync(harness, async () =>
        {
            var modal = new DeleteInstanceModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var delete = modal.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == harness.Localization.LibraryDelete);
                var point = delete.TranslatePoint(new Point(delete.Bounds.Width / 2, delete.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await viewModel.ConfirmDeleteModalCommand.ExecutionTask!;
                return shown;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.ModalDeleteInstanceTitle, texts);
        Assert.Contains(harness.Localization.FormatModalDeleteInstanceText("Alpha"), texts);
        Assert.False(viewModel.IsDeleteModalOpen);
        Assert.Empty(await harness.Services.Instances.GetAllAsync());
    }

    [Fact]
    public async Task PressBesideTheModal_CancelsItAndKeepsTheInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Alpha", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        viewModel.Instances.Single().BeginDeleteCommand.Execute(null);

        await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new DeleteInstanceModal(), DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            window.MouseDown(new Point(10, 10), MouseButton.Left);
            window.MouseUp(new Point(10, 10), MouseButton.Left);
            window.Close();
            return Task.FromResult(true);
        });

        Assert.False(viewModel.IsDeleteModalOpen);
        Assert.Single(await harness.Services.Instances.GetAllAsync());
    }
}
