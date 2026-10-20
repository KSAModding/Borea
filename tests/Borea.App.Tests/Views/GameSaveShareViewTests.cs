using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

/// <summary>Share in the menu of a vehicle, and the card it opens on the listing page.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class GameSaveShareViewTests
{
    [Fact]
    public async Task VehicleMenu_ShowsShareOnlyOnceTheIndexAcceptsVehicles()
    {
        using var gated = await ViewModelHarness.CreateAsync();
        using var ungated = await ViewModelHarness.CreateAsync(respond: GameSaveShareTests.Ungated);
        foreach (var harness in new[] { gated, ungated })
        {
            await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
            await GameSaveShareTests.OpenAsync(harness);
        }

        Assert.DoesNotContain(gated.Localization.GameSaveShare, await VehicleMenuAsync(gated));
        Assert.Contains(ungated.Localization.GameSaveShare, await VehicleMenuAsync(ungated));
    }

    [Fact]
    public async Task ListingPage_AfterShare_ShowsTheShareCardAndTheOptionalHints()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: GameSaveShareTests.Ungated);
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        var texts = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
            window.Close();
            return Task.FromResult(texts);
        });

        Assert.Contains(harness.Localization.ListingShareHeading, texts);
        Assert.Contains(harness.ViewModel.ListingEditor.ShareNotes.Single(), texts);
        Assert.Contains(harness.Localization.ListingShareSaveArchive, texts);
        Assert.Contains(harness.Localization.ListingForumsOptionalHint, texts);
        Assert.Contains(harness.Localization.ListingGameMinFromArchiveHint, texts);
        Assert.DoesNotContain(harness.Localization.ListingForumsHint, texts);
    }

    private static Task<List<string?>> VehicleMenuAsync(ViewModelHarness harness) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = 1280, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                var row = Assert.Single(harness.ViewModel.VehiclesSection.Items);
                var button = page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.DataContext == row && button.Flyout is MenuFlyout);
                button.Flyout!.ShowAt(button);
                page.UpdateLayout();
                var headers = ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => item.Header as string).ToList();
                button.Flyout.Hide();
                return Task.FromResult(headers);
            }
            finally
            {
                window.Close();
            }
        });
}
