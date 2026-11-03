using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views.Settings;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class GameSettingsLayoutTests
{
    [Fact]
    public async Task LoaderDirectoryField_KeepsItsWidth_WhenTheButtonTextsAreLong()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        Assert.True(harness.Localization.TrySetCulture("de"));

        var width = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 560, Height = 832, Content = new GameSettings(), DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var field = window.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => AutomationProperties.GetName(box) == harness.Localization.SetupLoaderDirectory);
                return Task.FromResult(field.Bounds.Width);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(width >= 400, $"The loader directory field is {width} px wide.");
    }

    [Fact]
    public async Task FolderAboveTheGame_ShowsThatItIsNotTheKsaFolderAndTheButtonForTheGameBelow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var game = Directory.CreateDirectory(Path.Combine(harness.Root, "ahwoo", "KSA")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
        File.WriteAllBytes(Path.Combine(game, "KSA.exe"), []);
        await viewModel.ChooseGameDirectoryAsync(Path.GetDirectoryName(game)!);

        var shown = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 560, Height = 832, Content = new GameSettings(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var button = window.GetVisualDescendants().OfType<Button>().Single(button => button.Command == viewModel.UseGameFolderBelowCommand);
                return Task.FromResult((Texts: texts, ButtonShown: button.IsEffectivelyVisible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.SetupNotGameFolder, shown.Texts);
        Assert.Contains(viewModel.GameFolderBelowText, shown.Texts);
        Assert.True(shown.ButtonShown);
    }
}
