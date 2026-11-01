using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views.Pages;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

/// <summary>Share in the menu of a vehicle, the steps it opens on the listing page, and the card of the full form.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class GameSaveShareViewTests
{
    [Fact]
    public async Task VehicleMenu_ShowsShareOnlyOnceTheIndexAcceptsVehicles()
    {
        using var gated = await ViewModelHarness.CreateAsync(respond: GameSaveShareTests.Gated);
        using var ungated = await ViewModelHarness.CreateAsync();
        foreach (var harness in new[] { gated, ungated })
        {
            await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
            await GameSaveShareTests.OpenAsync(harness);
        }

        Assert.DoesNotContain(gated.Localization.GameSaveShare, await VehicleMenuAsync(gated));
        Assert.Contains(ungated.Localization.GameSaveShare, await VehicleMenuAsync(ungated));
    }

    [Fact]
    public async Task ListingPage_AfterShare_ShowsTheDescribeStepWithItsOwnHeadingAndSidePanel()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        var texts = await PageTextsAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.GameSaveShare, texts);
        Assert.Contains(localization.ShareFlowTitle, texts);
        Assert.Contains(localization.ShareFlowIntro, texts);
        Assert.Contains(localization.ShareDescribeVehicle, texts);
        Assert.Contains(localization.ShareNext, texts);
        Assert.Contains(localization.NavigationBack, texts);
        Assert.Contains(localization.ShareChange, texts);
        Assert.Contains(harness.ViewModel.ListingEditor.ShareNextReason, texts);
        Assert.Contains(localization.ShareEditAll, texts);
        Assert.Contains(localization.SharePreviewVehicle, texts);
        Assert.Contains(localization.SharePreviewSentence, texts);
        Assert.Contains(localization.ShareStepChoose, texts);
        Assert.DoesNotContain(localization.DiscoverListYourMod, texts);
        Assert.DoesNotContain(localization.ListingShareHeading, texts);
        Assert.DoesNotContain(localization.ListingSteps, texts);
        Assert.DoesNotContain(localization.ListingErrorsHeading, texts);
        Assert.DoesNotContain(localization.ShareShortName, texts);
        var editor = harness.ViewModel.ListingEditor;
        Assert.Contains(localization.ShareModsHeading, texts);
        Assert.Contains(localization.ShareModsVehicleHint, texts);
        Assert.Contains(editor.Dependencies.Single().ModName, texts);
        Assert.Contains(editor.Dependencies.Single().ShareVersionText, texts);
        Assert.Contains(localization.ListingRemove, texts);
        Assert.Contains(editor.Dependencies.Single().NoteText, texts);
        Assert.Contains(editor.ShareNotes.Single(), texts);
        Assert.Contains(localization.ShareAddMod, texts);
        Assert.Contains(localization.DiscoverSearchPlaceholder, texts);
        Assert.DoesNotContain(localization.ListingShareHint, texts);
        Assert.DoesNotContain(localization.ShareModsNone, texts);
        Assert.DoesNotContain(localization.ShareModProblem, texts);
    }

    [Fact]
    public async Task ChooseStep_ShowsTheRowsWithTheirChipsAndTheNoteOfTheSelectedRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.RecordAsync(harness, instance, Path.Combine(harness.Services.Paths.GetInstanceVehiclesFolder(instance), "Example Rover"));
        await harness.Services.Instances.SetActiveInstanceAsync(instance);
        harness.IsGameProcessRunning = () => true;
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);
        await harness.ViewModel.ListingEditor.ShareRowCompare;

        var texts = await PageTextsAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ShareChooseHeading, texts);
        Assert.Contains(localization.ShareChooseHint, texts);
        Assert.Contains(localization.ContentTypeVehicle, texts);
        Assert.Contains(localization.ContentTypeSave, texts);
        Assert.Contains(localization.ShareGameRunning, texts);
        Assert.Contains("Main", texts);
        Assert.Contains(localization.LibraryActiveHeading, texts);
        Assert.Contains("Example Rover", texts);
        Assert.Contains(localization.FormatSharePickFrom("ExampleRover"), texts);
        Assert.Contains(localization.FormatShareUnchangedCopy("ExampleRover"), texts);
        Assert.Contains(localization.ShareNext, texts);
        Assert.DoesNotContain(localization.ShareSaveHoldsAll, texts);
        Assert.DoesNotContain(localization.SharePreviewVehicle, texts);
        Assert.DoesNotContain(localization.NavigationBack, texts);
    }

    [Fact]
    public async Task ChooseStep_OnAPageThatStandsBeforeTheInstancesLoad_OffersNoNewInstanceWhenOneExists()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);

        var texts = await PageTextsAsync(harness, async () =>
        {
            await harness.ViewModel.LoadAsync();
            await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);
        });

        Assert.Contains(harness.Localization.ShareNoVehicles, texts);
        Assert.DoesNotContain(harness.Localization.DiscoverCreateInstance, texts);
    }

    [Fact]
    public async Task ChooseStep_InstanceCreatedFromTheEmptyList_MovesToTheTextOfThatInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var texts = await PageTextsAsync(harness, async () =>
        {
            await viewModel.LoadAsync();
            await viewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);
            viewModel.BeginCreateInstanceCommand.Execute(null);
            viewModel.ModalInstanceName = "Main";
            await viewModel.ConfirmNameModalCommand.ExecuteAsync(null);
        });

        Assert.Contains(harness.Localization.ShareNoVehicles, texts);
        Assert.DoesNotContain(harness.Localization.ShareNoInstance, texts);
        Assert.DoesNotContain(harness.Localization.DiscoverCreateInstance, texts);
    }

    [Fact]
    public async Task ChooseStep_WithNoInstance_ShowsItsTextAndOneActionAndNoNext()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.OpenShareFlowAsync(GameSaveKind.Vehicle);

        var texts = await PageTextsAsync(harness);

        Assert.Contains(harness.Localization.ShareNoInstance, texts);
        Assert.Contains(harness.Localization.DiscoverCreateInstance, texts);
        Assert.DoesNotContain(harness.Localization.ShareNext, texts);
    }

    [Fact]
    public async Task RowWithNoId_SaysThatItNeedsAFix()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        harness.ViewModel.ListingEditor.Dependencies.Single().Id = string.Empty;

        Assert.Contains(harness.Localization.ShareModProblem, await PageTextsAsync(harness));
    }

    [Fact]
    public async Task NewVersion_ShowsTheModsCardButNotTheFields()
    {
        using var harness = await ShareHostShareTests.ListedRoverHarnessAsync();
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;

        var question = await PageTextsAsync(harness);

        Assert.Contains(editor.ShareLooksLikeText, question);
        Assert.Contains(editor.ShareAsNewVersionText, question);
        Assert.Contains(harness.Localization.ShareIsDifferentVehicle, question);
        Assert.DoesNotContain(harness.Localization.ShareYourName, question);
        Assert.DoesNotContain(harness.Localization.ShareModsHeading, question);
        Assert.DoesNotContain(harness.Localization.ShareNext, question);
        Assert.DoesNotContain(harness.Localization.ShareEditAll, question);

        editor.ShareAsNewVersionCommand.Execute(null);
        var texts = await PageTextsAsync(harness);

        Assert.True(editor.IsNewVersionShare);
        Assert.Contains(harness.Localization.ShareModsHeading, texts);
        Assert.DoesNotContain(harness.Localization.ShareYourName, texts);
        Assert.DoesNotContain(harness.Localization.ShareMoreOptions, texts);
    }

    [Fact]
    public async Task ListingPage_AfterShare_ShowsTheShareCardAndTheOptionalHints()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        harness.ViewModel.ListingEditor.EditAllShareFieldsCommand.Execute(null);

        var texts = await PageTextsAsync(harness);

        Assert.Contains(harness.Localization.ShareBackToSteps, texts);
        Assert.Contains(harness.Localization.ListingShareHeading, texts);
        Assert.Contains(harness.ViewModel.ListingEditor.ShareNotes.Single(), texts);
        Assert.Contains(harness.Localization.ListingShareArchiveHeading, texts);
        Assert.Contains(harness.Localization.ListingShareArchiveIntro, texts);
        Assert.Contains(harness.Localization.ListingShareSaveArchive, texts);
        Assert.Contains(harness.Localization.DiscoverSearchPlaceholder, texts);
        Assert.Contains(harness.Localization.ListingForumsOptionalHint, texts);
        Assert.Contains(harness.Localization.ListingGameMinFromArchiveHint, texts);
        Assert.DoesNotContain(harness.Localization.ListingForumsHint, texts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishStep_ShowsTheSummaryAndTheLimitOfEachHost(bool hasSeveralHosts)
    {
        const string host = """
            "ksamods": { "kind": "share", "name": "ksamods.gg", "api": "https://ksamods.gg/api/share/v1/", "types": ["vehicle"], "max_bytes": { "vehicle": 10485760 } }
            """;
        const string other = """
            "another": { "kind": "share", "name": "another.example", "api": "https://another.example/api/share/v1/", "types": ["vehicle"], "max_bytes": { "vehicle": 20971520 } }
            """;
        var hosts = "{" + host + (hasSeveralHosts ? "," + other : string.Empty) + "}";
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.ShareHosts(hosts));
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        await editor.ShareHostsLoad;
        editor.ShareFlowStep = ShareFlowStep.Publish;

        var texts = await PageTextsAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ShareEdit, texts);
        Assert.Contains(editor.ShareSummaryModsText, texts);
        Assert.Contains(editor.ShareNeedsBuildText, texts);
        Assert.Contains(editor.ShareHostHeadingText, texts);
        Assert.Contains(localization.FormatShareHostLimitVehicle("10 MB"), texts);
        Assert.Equal(hasSeveralHosts, texts.Contains(localization.FormatShareHostLimitVehicle("20 MB")));
        Assert.Contains(editor.ShareCheckOnHostText, texts);
        Assert.Contains(editor.ShareCheckHintText, texts);
        Assert.Contains(localization.ShareSaveZipInstead, texts);
        Assert.Contains(localization.ShareSaveZipInsteadHint, texts);
        Assert.DoesNotContain(localization.ShareRenameAndCheck, texts);
    }

    [Fact]
    public async Task PublishStep_WithNoHost_OffersTheZipFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);
        var editor = harness.ViewModel.ListingEditor;
        await editor.ShareHostsLoad;
        editor.ShareFlowStep = ShareFlowStep.Publish;

        var texts = await PageTextsAsync(harness);

        Assert.Contains(editor.ShareNoHostText, texts);
        Assert.Contains(harness.Localization.ShareSaveZipInsteadHint, texts);
    }

    [Fact]
    public async Task DoneStep_ReplacesThePublishStep_AndKeepsTheRequestInClosedDetails()
    {
        using var harness = await ShareHostShareTests.PublishedHarnessAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.ShareHostError = "No browser.";

        var texts = await PageTextsAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.FormatShareDoneHeading("Example Rover"), texts);
        Assert.Contains(localization.ShareDoneNow, texts);
        Assert.Contains(localization.FormatShareOpenHost("ksamods.gg"), texts);
        Assert.Contains(localization.ContentCopyLink, texts);
        Assert.Contains(localization.ShareAnother, texts);
        Assert.Contains(localization.ShareDoneNewListing, texts);
        Assert.Contains("No browser.", texts);
        Assert.Contains(localization.ShareTechnicalDetails, texts);
        Assert.DoesNotContain(localization.ShareShowRequest, texts);
        Assert.DoesNotContain(editor.ShareCheckOnHostText, texts);
        Assert.DoesNotContain(localization.ShareSaveZipInsteadHint, texts);
    }

    [Fact]
    public async Task EditAllFields_OpensTheFormAtItsTop()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
        await GameSaveShareTests.OpenAsync(harness);
        await Assert.Single(harness.ViewModel.VehiclesSection.Items).ShareCommand.ExecuteAsync(null);

        var (before, after) = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 300, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var scroller = page.FindControl<ScrollViewer>("PageScroller")!;
            scroller.Offset = new Vector(0, 100);
            var scrolled = scroller.Offset.Y;
            harness.ViewModel.ListingEditor.EditAllShareFieldsCommand.Execute(null);
            window.UpdateLayout();
            var opened = scroller.Offset.Y;
            window.Close();
            return Task.FromResult((scrolled, opened));
        });

        Assert.Equal(100, before);
        Assert.Equal(0, after);
    }

    private static Task<List<string?>> PageTextsAsync(ViewModelHarness harness, Func<Task>? afterShow = null) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            if (afterShow is not null)
                await afterShow();

            window.UpdateLayout();
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
            window.Close();
            return texts;
        });

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
