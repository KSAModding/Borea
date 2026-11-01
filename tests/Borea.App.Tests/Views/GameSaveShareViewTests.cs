using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;
using Borea.Core.Mods;
using ContentPage = Borea.App.Views.Pages.ContentPage;

namespace Borea.App.Tests.Views;

/// <summary>Share on the row and in the menu of a vehicle, the steps it opens on the listing page, the card that starts them, and the card of the full form.</summary>
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
    public async Task RowButton_ShowsOnlyForAnOwnIdleItemThatTheIndexAccepts()
    {
        using var gated = await ViewModelHarness.CreateAsync(respond: GameSaveShareTests.Gated);
        using var ungated = await ViewModelHarness.CreateAsync();
        foreach (var harness in new[] { gated, ungated })
        {
            await GameSaveShareTests.CreateInstanceAsync(harness, "Example Rover");
            await GameSaveShareTests.OpenAsync(harness);
        }

        var whenGated = await RowButtonShowsAsync(gated, _ => { });
        var whenAccepted = await RowButtonShowsAsync(ungated, _ => { });
        var whileConfirming = await RowButtonShowsAsync(ungated, row => row.IsConfirmingDelete = true);
        var afterConfirming = await RowButtonShowsAsync(ungated, row => row.IsConfirmingDelete = false);
        var withRecord = await RowButtonShowsAsync(ungated, row => row.ShowRecord(new InstalledContentRecord(Release, row.FolderName, DateTimeOffset.UnixEpoch, new Dictionary<string, string>()), listing: null, newer: null));

        Assert.False(whenGated);
        Assert.True(whenAccepted);
        Assert.False(whileConfirming);
        Assert.True(afterConfirming);
        Assert.False(withRecord);
    }

    [Fact]
    public async Task ListingStart_ItsFirstCardOpensTheFirstStepOnVehicles_AndTheCardsTakeTheRoomOfTheSidePanel()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.OpenListingAsync();
        harness.ViewModel.ShowDiscoverSavesCommand.Execute(null);

        var (firstHeading, hadSidePanel, hasSidePanel) = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var body = page.GetVisualDescendants().OfType<PageBodyPanel>().First();
            var hadPanel = body.HasSidePanel;
            var heading = page.GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible && text.Classes.Contains("heading-md")).Text;
            Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Command == harness.ViewModel.ShareFromListingStartCommand);
            await harness.ViewModel.ShareFromListingStartCommand.ExecuteAsync(null);
            window.UpdateLayout();
            var hasPanel = body.HasSidePanel;
            window.Close();
            return (heading, hadPanel, hasPanel);
        });

        Assert.Equal(harness.Localization.ListingShareCardTitle, firstHeading);
        Assert.False(hadSidePanel);
        Assert.True(hasSidePanel);
        var editor = harness.ViewModel.ListingEditor;
        Assert.True(editor.IsShareChoose);
        Assert.True(editor.IsShareChooseVehicle);
    }

    [Fact]
    public async Task ListingStart_TheCardFollowsTheGate_AndOpensOnTheTypeThatTheIndexAccepts()
    {
        using var noVehicles = await ViewModelHarness.CreateAsync(respond: request => GameSaveShareTests.GatedOn(request, "vehicle"));
        using var noTypes = await ViewModelHarness.CreateAsync(respond: GameSaveShareTests.Gated);
        await noVehicles.ViewModel.OpenListingAsync();
        await noTypes.ViewModel.OpenListingAsync();

        Assert.True(await StartCardShowsAsync(noVehicles));
        Assert.False(await StartCardShowsAsync(noTypes));

        await noVehicles.ViewModel.ShareFromListingStartCommand.ExecuteAsync(null);
        Assert.True(noVehicles.ViewModel.ListingEditor.IsShareChooseSave);
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
    public async Task PublishStep_NamesThePictures_AndTheDoneStepShowsHowTheyEnded()
    {
        using var waiting = await ShareHostShareTests.PictureHarnessAsync(publish: false);
        using var published = await ShareHostShareTests.PictureHarnessAsync(publish: true);

        Assert.Contains(waiting.Localization.FormatShareSummaryPictures(2), await PageTextsAsync(waiting));

        var texts = await PageTextsAsync(published);
        Assert.Contains("shot1.png", texts);
        Assert.Contains("shot2.png", texts);
        Assert.Contains(published.Localization.SharePicturesSent, texts);
        Assert.DoesNotContain(published.Localization.DiscoverIndexRetry, texts);
    }

    [Fact]
    public async Task PublishStep_ShowsTheCoverThatBoreaDrew_AndSaysThatItBecomesPublic()
    {
        using var harness = await ShareHostShareTests.CoverHarnessAsync();

        var texts = await PageTextsAsync(harness);

        Assert.Contains(harness.Localization.ShareCoverHint, texts);
        Assert.Contains(harness.Localization.ShareCoverOwn, texts);
        Assert.Contains(harness.Localization.ShareCoverLeaveOut, texts);
        Assert.DoesNotContain(harness.Localization.ShareCoverLeftOut, texts);
    }

    [Fact]
    public async Task DoneStep_HidesWhatWouldDropThePictures_WhileTheyGoUp()
    {
        var (harness, publish) = await ShareHostShareTests.HeldPictureHarnessAsync();
        using (harness)
        {
            var texts = await PageTextsAsync(harness);

            Assert.Contains(harness.Localization.LibraryCancel, texts);
            Assert.DoesNotContain(harness.Localization.ListingStartOver, texts);
            Assert.DoesNotContain(harness.Localization.ShareAnother, texts);

            harness.ViewModel.ListingEditor.CancelShareHostCommand.Execute(null);
            await publish;
        }
    }

    [Fact]
    public async Task DescribeStep_ShowsThePicturesWithTheCover()
    {
        using var harness = await ShareHostShareTests.PictureHarnessAsync(publish: false);
        harness.ViewModel.ListingEditor.BackToShareDescribeCommand.Execute(null);

        var texts = await PageTextsAsync(harness);

        Assert.Contains(harness.Localization.FormatSharePicturesHint(6, "10 MB"), texts);
        Assert.Contains("shot1.png", texts);
        Assert.Contains(harness.Localization.SharePictureCover, texts);
        Assert.Contains(harness.Localization.SharePictureAdd, texts);
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
        Assert.Contains(localization.ShareManageChange, texts);
        Assert.Contains(localization.FormatShareTakeDownVersion("1.0.0"), texts);
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

    private static readonly ModVersionMetadata Release = new(1, "ExampleRover", ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.9.7.5402", 5402,
        new DownloadInfo("https://example.invalid/rover.zip", new string('A', 64), null, "application/zip"), null, [], type: ContentType.Vehicle, install: new InstallInfo("Example Rover", derived: true, folder: "Example Rover"));

    /// <summary>Changes the row while the page shows, so the answer also proves that the button hears of the change.</summary>
    private static Task<bool> RowButtonShowsAsync(ViewModelHarness harness, Action<GameSaveItem> change) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = 1280, Height = 900, DataContext = harness.ViewModel, Content = page };
            window.Show();
            page.UpdateLayout();
            var row = Assert.Single(harness.ViewModel.VehiclesSection.Items);
            change(row);
            page.UpdateLayout();
            var shows = page.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && button.Command == row.ShareCommand);
            window.Close();
            return Task.FromResult(shows);
        });

    private static Task<bool> StartCardShowsAsync(ViewModelHarness harness) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var shows = page.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && button.Command == harness.ViewModel.ShareFromListingStartCommand);
            window.Close();
            return Task.FromResult(shows);
        });

    [Fact]
    public async Task ContentPage_OwnListing_ShowsTheWaysToManageIt()
    {
        using var harness = await ShareHostShareTests.OwnedPageHarnessAsync();
        var localization = harness.Localization;

        var texts = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ContentPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var shown = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
            window.Close();
            return Task.FromResult(shown);
        });

        Assert.Contains(localization.ShareManageHeading, texts);
        Assert.Contains(localization.ShareManageChange, texts);
        Assert.Contains(localization.ShareManageTakeDown, texts);
        Assert.Contains(localization.FormatShareManageOnHost("ksamods.gg"), texts);
    }

    [Fact]
    public async Task ManageModal_TakeDown_ShowsTheWarningAndTheButtonOnlyOnceAVersionIsChosen()
    {
        using var harness = await ShareHostShareTests.OwnedPageHarnessAsync();
        var localization = harness.Localization;
        await harness.ViewModel.TakeDownContentVersionCommand.ExecuteAsync(null);
        var dialog = harness.ViewModel.ShareManage!;

        var (before, buttonBefore, after, buttonAfter) = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new ShareManageModal();
            var window = new Window { Width = 1280, Height = 480, Content = modal, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var shownBefore = Shown(modal);
            var hasButtonBefore = ShowsTakeDownButton(modal);
            dialog.SelectedVersion = "1.2.0";
            window.UpdateLayout();
            var shownAfter = Shown(modal);
            var hasButtonAfter = ShowsTakeDownButton(modal);
            window.Close();
            return Task.FromResult((shownBefore, hasButtonBefore, shownAfter, hasButtonAfter));
        });

        Assert.Contains(localization.ShareTakeDownChoose, before);
        Assert.False(buttonBefore);
        Assert.Contains(localization.FormatShareTakeDownWarning("1.2.0", "Example Rover", "ksamods.gg"), after);
        Assert.True(buttonAfter);
        Assert.Contains(localization.FormatShareManageOnHost("ksamods.gg"), after);

        bool ShowsTakeDownButton(Control modal) =>
            modal.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && button.Command == dialog.TakeDownCommand);

        static List<string?> Shown(Control modal) =>
            [.. modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)];
    }

    [Fact]
    public async Task ManageModal_TakeDown_NoKnownVersion_SaysSo_AndKeepsTheLink()
    {
        using var harness = await ShareHostShareTests.OwnedPageWithoutVersionsHarnessAsync();
        var localization = harness.Localization;
        await harness.ViewModel.TakeDownContentVersionCommand.ExecuteAsync(null);

        var texts = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new ShareManageModal();
            var window = new Window { Width = 1280, Height = 480, Content = modal, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
            window.Close();
            return Task.FromResult(shown);
        });

        Assert.Contains(localization.ShareTakeDownNoVersion, texts);
        Assert.Contains(localization.FormatShareManageOnHost("ksamods.gg"), texts);
    }

    /// <summary>The fields in the order of the modal, with the license box that shows for another license.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ManageModal_Change_EscapeInAField_Closes(int field)
    {
        using var harness = await ShareHostShareTests.OwnedPageHarnessAsync();
        var viewModel = harness.ViewModel;
        viewModel.ChangeContentOnHostCommand.Execute(null);
        var dialog = viewModel.ShareManage!;
        dialog.SelectedLicense = dialog.LicenseChoices[^1];

        var isFocused = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new ShareManageModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            window.UpdateLayout();
            var focused = modal.GetVisualDescendants().OfType<TextBox>().Where(box => box.Classes.Contains("field")).ElementAt(field).Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.Close();
            return Task.FromResult(focused);
        });

        Assert.True(isFocused);
        Assert.False(viewModel.IsShareManageOpen);
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
