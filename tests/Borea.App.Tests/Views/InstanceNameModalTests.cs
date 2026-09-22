using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class InstanceNameModalTests
{
    [Fact]
    public async Task Explanation_ShowsWhenTheModalCreatesAndNotWhenItRenames()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var explanation = harness.Localization.ModalInstanceExplanation;

        var shown = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new InstanceNameModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = modal };
            window.Show();

            viewModel.BeginCreateInstanceCommand.Execute(null);
            window.UpdateLayout();
            var creating = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            viewModel.BeginImportSharedProfileCommand.Execute(null);
            window.UpdateLayout();
            var importing = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            viewModel.Instances.Single().BeginRenameCommand.Execute(null);
            window.UpdateLayout();
            var renaming = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            window.Close();
            return Task.FromResult((creating, importing, renaming));
        });

        Assert.Contains(explanation, shown.creating);
        Assert.Contains(explanation, shown.importing);
        Assert.Contains(harness.Localization.ModalRenameInstanceTitle, shown.renaming);
        Assert.DoesNotContain(explanation, shown.renaming);
    }

    private static List<string?> Texts(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
}
