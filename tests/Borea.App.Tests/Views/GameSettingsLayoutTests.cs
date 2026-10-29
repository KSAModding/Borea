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
}
