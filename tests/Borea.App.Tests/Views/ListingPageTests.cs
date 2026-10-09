using System.Net;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Listings;
using Borea.Network.GitHub;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ListingPageTests
{
    [Fact]
    public async Task StartStep_ShowsBothWaysIn()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.OpenListingAsync();

        var texts = await RenderAsync(harness);

        Assert.Contains(harness.Localization.ListingNewTitle, texts);
        Assert.Contains(harness.Localization.ListingPackTitle, texts);
        Assert.Contains(harness.Localization.ListingStartPack, texts);
        Assert.Contains(harness.Localization.ListingChangeTitle, texts);
        Assert.DoesNotContain(harness.Localization.ListingSteps, texts);
    }

    [Fact]
    public async Task SearchField_TypingTheArrowKeysAndEnter_LoadTheChosenListing()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeStarMap);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        var chosen = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            page.FindControl<TextBox>("ListedSearch")!.Focus();
            window.KeyTextInput("s");
            foreach (var key in new[] { PhysicalKey.ArrowDown, PhysicalKey.ArrowDown, PhysicalKey.ArrowUp, PhysicalKey.ArrowDown })
                Press(window, key);
            var chosen = editor.SelectedListed?.Id;
            Press(window, PhysicalKey.Enter);
            await (editor.LoadListedCommand.ExecutionTask ?? Task.CompletedTask);
            window.Close();
            return chosen;
        });

        Assert.Equal("s", editor.ListedQuery);
        Assert.Equal("StarMap", chosen);
        Assert.True(editor.IsFormStep);
        Assert.Equal("StarMap", editor.Id);
    }

    [Fact]
    public async Task ResultList_Enter_LoadsTheChosenListing()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeStarMap);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            page.FindControl<ListBox>("ListedResults")!.GetVisualDescendants().OfType<ListBoxItem>().Last().Focus();
            Press(window, PhysicalKey.ArrowUp);
            Press(window, PhysicalKey.ArrowDown);
            Press(window, PhysicalKey.Enter);
            await (editor.LoadListedCommand.ExecutionTask ?? Task.CompletedTask);
            window.Close();
            return 0;
        });

        Assert.True(editor.IsFormStep);
        Assert.Equal("StarMap", editor.Id);
    }

    [Fact]
    public async Task ResultList_DoubleClick_LoadsTheListing()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeStarMap);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            var item = page.FindControl<ListBox>("ListedResults")!.GetVisualDescendants().OfType<ListBoxItem>().Last();
            var center = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            for (var click = 0; click < 2; click++)
            {
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
            }

            await (editor.LoadListedCommand.ExecutionTask ?? Task.CompletedTask);
            window.Close();
            return 0;
        });

        Assert.True(editor.IsFormStep);
        Assert.Equal("StarMap", editor.Id);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task StartStep_SearchFieldAndResultsFitTheCard(double windowWidth)
    {
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, editSnapshot: json => json.Replace("StarMapLoader/StarMap", "octocat/StarMap", StringComparison.Ordinal));
        await harness.ViewModel.OpenListingAsync();

        var layout = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = 1080, Content = body, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            var search = page.FindControl<TextBox>("ListedSearch")!;
            var results = page.FindControl<ListBox>("ListedResults")!;
            var card = search.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("card"));
            var rows = results.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            var controls = new List<Control> { search, results }
                .Concat(search.GetVisualParent()!.GetVisualChildren().OfType<Button>())
                .Concat(rows.SelectMany(row => row.GetVisualDescendants().OfType<Control>().Where(control => control is TextBlock || control.Classes.Contains("chip"))))
                .Where(control => control.IsEffectivelyVisible)
                .ToList();
            var result = new SearchLayout(
                rows.Count,
                controls.Count(control => control is Border),
                controls.Where(control => !Fits(control, card)).Select(control => control.ToString() ?? string.Empty).ToList(),
                controls.OfType<TextBlock>().Any(text => text.TextLayout.TextLines.Any(line => line.HasCollapsed)));
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal(4, layout.Rows);
        Assert.Equal(1, layout.OwnChips);
        Assert.Equal([], layout.Outside);
        Assert.False(layout.Trimmed);
    }

    [Fact]
    public async Task FormStep_ShowsTheFieldsTheRowsTheChecksAndTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: new GitHubSession(new HttpClient(), "", ""));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.AddIconCommand.Execute(null);
        editor.AddDescriptionImageCommand.Execute(null);
        editor.AddDependencyCommand.Execute(null);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingAbout, texts);
        Assert.Contains(localization.ListingImageUrlHint, texts);
        Assert.Contains(localization.ListingImageNotMeasured, texts);
        Assert.Contains(localization.ListingPreview, texts);
        Assert.Contains(localization.ListingSteps, texts);
        Assert.Contains(localization.ListingDependencies, texts);
        Assert.Contains(editor.MissingText, texts);
        Assert.DoesNotContain(localization.ListingErrorsHeading, texts);
        Assert.Contains(localization.ListingOpenPullRequest, texts);
        Assert.Contains(localization.ListingNewPullRequestText, texts);
        Assert.DoesNotContain(localization.ListingEditPullRequestText, texts);
    }

    [Fact]
    public async Task DependencyCard_ShowsTheDeclaredDependencies_AndTheVersionFieldsKeepTheirText()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
            request.RequestUri!.AbsolutePath.EndsWith("/listings/AdvancedFlightComputer.toml", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListingDependencyHelpTests.AdvancedFlightComputerListing) }
                : null);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "advanced";
        await editor.LoadListedCommand.ExecuteAsync(null);
        editor.AddDependencyEntry("MeasureTools", "conflict");
        var row = editor.Dependencies.Single();
        row.Min = "0.5";

        var (texts, versionTexts) = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
            var versionTexts = page.GetVisualDescendants().OfType<ComboBox>().Where(box => box.IsEditable).Select(box => box.Text).ToList();
            window.Close();
            return Task.FromResult((texts, versionTexts));
        });

        var localization = harness.Localization;
        Assert.Contains(localization.FormatListingDeclared("0.7.5"), texts);
        Assert.Contains(localization.FormatListingDeclaredOptional("KittenExtensions"), texts);
        Assert.Contains(localization.ListingAddBounds, texts);
        Assert.Contains(localization.ListingReadArchive, texts);
        Assert.Contains(localization.ListingDependencyKindConflict, texts);
        Assert.DoesNotContain(localization.ListingNeedsNewest, texts);
        Assert.Equal(["0.5", ""], versionTexts);
        Assert.Equal(("conflict", "0.5", ""), (row.Kind, row.Min, row.Max));
    }

    [Fact]
    public async Task DescriptionPreview_DrawsTheDescriptionAsTheModPageDoes()
    {
        const string images = @"![The settings window](ksa-image:settings-window)\n\n![The old map](ksa-image:map-view) and ![Gone](ksa-image:gone)\n\n";
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => ImageViewModelTests.WithImages("AdvancedFlightComputer", $$"""{ "description": [{{ImageViewModelTests.Description("settings-window")}}] }""")(
            json.Replace("\"description\": \"Adds quick-tools", $"\"description\": \"{images}Adds quick-tools", StringComparison.Ordinal)));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        var description = viewModel.SelectedContent!.Description!;
        var modPage = await HeadlessApp.RunAsync(harness, () => Task.FromResult(DrawnDescription(new Borea.App.Views.Pages.ContentPage(), harness)));

        await viewModel.OpenListingAsync();
        viewModel.ListingEditor.Load(new ListingDraft
        {
            Description = description,
            DescriptionImages =
            [
                new ListingImageRecord("https://images.example/settings-window.png") { Id = "settings-window", Sha256 = new string('A', 64), Width = 1600, Height = 900, Size = 400_000 },
                new ListingImageRecord("https://images.example/map-view.png") { Id = "map-view" },
            ],
        });
        viewModel.ListingEditor.IsDescriptionPreviewOn = true;
        var preview = await HeadlessApp.RunAsync(harness, () => Task.FromResult(DrawnDescription(new ListingPage(), harness)));

        Assert.StartsWith("![The settings window]", description, StringComparison.Ordinal);
        Assert.Contains("ListingImageView. 1 https://images.example/settings-window.png", preview);
        Assert.Contains("Border.thumbnail 1 The old map", preview);
        Assert.Contains("Border.thumbnail 1 Gone", preview);
        Assert.Equal(modPage, preview);
    }

    [Fact]
    public async Task DescriptionPreview_TheSwitchShowsItAndTypingUpdatesIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Description = "Typed";

        var (before, shown, sideBySide, typed, after, restored, kept) = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            // the scroll area of a hidden preview has no visual children yet, so the view is found in the logical tree
            var preview = page.GetLogicalDescendants().OfType<MarkdownView>().Single(view => view.Images is not null);
            var toggle = page.GetVisualDescendants().OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == harness.Localization.ListingDescriptionPreview);
            var before = preview.IsEffectivelyVisible;

            var field = page.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == harness.Localization.ListingDescription);
            var fullWidth = field.Bounds.Width;

            Click(window, toggle);
            window.UpdateLayout();
            var shown = (preview.IsEffectivelyVisible, MarkdownViewTests.Drawn(preview).Last());
            var fieldLeft = field.TranslatePoint(default, window)!.Value;
            var previewLeft = preview.TranslatePoint(default, window)!.Value;
            var sideBySide = (field.Bounds.Width < fullWidth / 2 + 1, previewLeft.X > fieldLeft.X + field.Bounds.Width, previewLeft.Y < fieldLeft.Y + field.Bounds.Height);

            field.Focus();
            field.CaretIndex = field.Text!.Length;
            window.KeyTextInput(" more");
            window.UpdateLayout();
            var typed = MarkdownViewTests.Drawn(preview).Last();
            var shownChildren = preview.Children.Count;

            Click(window, toggle);
            window.UpdateLayout();
            var after = preview.IsEffectivelyVisible;
            var restored = Math.Abs(field.Bounds.Width - fullWidth) < 1;
            var kept = shownChildren > 0 && preview.Children.Count == shownChildren;
            window.Close();
            return Task.FromResult((before, shown, sideBySide, typed, after, restored, kept));
        });

        Assert.False(before);
        Assert.True(shown.IsEffectivelyVisible);
        Assert.EndsWith("[Typed]", shown.Item2, StringComparison.Ordinal);
        Assert.Equal((true, true, true), sideBySide);
        Assert.EndsWith("[Typed more]", typed, StringComparison.Ordinal);
        Assert.Equal("Typed more", editor.Description);
        Assert.False(after);
        Assert.True(restored, "the field takes the whole width again");
        Assert.True(kept, "turning the preview off hides its view and removes none of its controls");
        Assert.False(editor.IsDescriptionPreviewOn);
    }

    /// <summary>What the visible description view of the page draws, with its images resolved.</summary>
    private static List<string> DrawnDescription(UserControl page, ViewModelHarness harness)
    {
        page.DataContext = harness.ViewModel;
        var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
        window.Show();
        window.UpdateLayout();
        var drawn = MarkdownViewTests.Drawn(page.GetVisualDescendants().OfType<MarkdownView>().Single(view => view.IsEffectivelyVisible && view.Images is not null));
        window.Close();
        return drawn;
    }

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }

    [Fact]
    public async Task SignedOut_OffersThePullRequestWithTheSignInAndTheBrowserPath()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: new ListingPullRequestViewModelTests.FakeSession(), listingPublisher: new ListingPullRequestViewModelTests.FakePublisher());
        await harness.ViewModel.OpenListingAsync();
        harness.ViewModel.ListingEditor.StartEmptyCommand.Execute(null);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingSignInText, texts);
        Assert.Contains(localization.ListingPublish, texts);
        Assert.Contains(localization.ListingOpenInBrowser, texts);
        Assert.DoesNotContain(localization.ListingSignIn, texts);
        Assert.DoesNotContain(localization.ListingNewPullRequestText, texts);
    }

    [Fact]
    public async Task PackForm_ShowsThePackItsMembersTheOwnerFileAndTheSignedInPublish()
    {
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, listingPublisher: new ListingPullRequestViewModelTests.FakePublisher());
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        ListingEditorTests.FillPack(editor);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingPackTitle, texts);
        Assert.Contains(localization.ListingPack, texts);
        Assert.Contains(localization.ListingMembers, texts);
        Assert.Contains("AdvancedFlightComputer", texts);
        Assert.Contains(localization.FormatListingOwnerFileText("packs/my-pack/owner.json"), texts);
        Assert.Contains(localization.ListingCopyOwnerFile, texts);
        Assert.Contains(localization.ListingSaveOwnerFile, texts);
        Assert.Contains(localization.ListingPublish, texts);
        Assert.Contains(localization.ListingOpenInBrowser, texts);
        Assert.DoesNotContain(localization.FormatListingNewPackPullRequestText("packs/my-pack/1.0.0.toml", "packs/my-pack/owner.json"), texts);
        Assert.DoesNotContain(localization.ListingReleases, texts);
        Assert.DoesNotContain(localization.ListingDependencies, texts);
        Assert.DoesNotContain(localization.ListingUsesLoader, texts);
    }

    [Fact]
    public async Task NoFork_ShowsOnlyThatStep()
    {
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        var publisher = new ListingPullRequestViewModelTests.FakePublisher
        {
            Failure = new ListingPublishException(ListingPublishFailure.NoFork, ListingPublishStep.Fork),
            Ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "owner/MyMod"),
        };
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, listingPublisher: publisher);
        var editor = harness.ViewModel.ListingEditor;
        editor.OwnershipDelay = TimeSpan.Zero;
        await harness.ViewModel.OpenListingAsync();
        FillValidListing(editor);
        await editor.OwnershipCheck;
        await editor.PublishCommand.ExecuteAsync(null);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingForkMissing, texts);
        Assert.Contains(localization.ListingMakeFork, texts);
        Assert.Contains(localization.ListingOwnershipSteward, texts);
        Assert.Single(texts, text => text == localization.ListingCheckAgain);
        Assert.DoesNotContain(localization.ListingAllowOnFork, texts);
    }

    [Fact]
    public async Task SignedIn_FollowedPullRequest_ShowsTheOwnershipTheStateAndTheVerdict()
    {
        const string verdict = "The validation rejected this change.";
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        var publisher = new ListingPullRequestViewModelTests.FakePublisher();
        publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.Rejected, verdict));
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, listingPublisher: publisher);
        var editor = harness.ViewModel.ListingEditor;
        editor.OwnershipDelay = TimeSpan.Zero;
        editor.FollowInterval = TimeSpan.FromHours(1);
        await harness.ViewModel.OpenListingAsync();
        FillValidListing(editor);
        await editor.OwnershipCheck;
        await editor.PublishCommand.ExecuteAsync(null);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (editor.PullRequestStatus is null)
                await Task.Delay(10, timeout.Token);
        }

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingOwnershipVerified, texts);
        Assert.Contains(localization.ListingUpdatePullRequest, texts);
        Assert.Contains(localization.ListingOpenInBrowser, texts);
        Assert.Contains("Pull request #90", texts);
        Assert.Contains(localization.ListingStateRejected, texts);
        Assert.Contains(verdict, texts);
        Assert.DoesNotContain(localization.ListingSignIn, texts);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    public async Task MemberRow_ChosenReleaseWithAMark_KeepsTheNameTheBoxAndTheRemoveButtonInTheCard(double windowWidth)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: MarkedMeasureTools);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        LoadMarkedPack(editor);

        var layout = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            Grid.SetColumn(page, 1);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PageBodyPanel.NavigationRailWidth},*"), Children = { page } };
            var window = new Window { Width = windowWidth, Height = 1080, Content = body, DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var card = page.GetVisualDescendants().OfType<Border>().First(border => Equals(border.Tag, "members"));
                var box = card.GetVisualDescendants().OfType<ComboBox>().Single(box => box.DataContext is ListingPackMemberRow { Id: "MeasureTools" });
                var header = (Grid)box.GetVisualParent()!;
                var texts = header.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().ToList();
                var remove = header.Children.OfType<Button>().Single();
                var mark = card.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == ((ListingPackMemberRow)box.DataContext!).SelectedMark && text.IsEffectivelyVisible && !text.GetVisualAncestors().OfType<ComboBox>().Any());

                box.IsDropDownOpen = true;
                await HeadlessApp.FramesAsync(5);
                var item = (Control)box.ContainerFromIndex(0)!;
                var result = new MemberRowLayout(
                    texts[0].Bounds.Width,
                    texts[1].TextLayout.TextLines.Any(line => line.HasCollapsed),
                    new Control[] { box, remove, mark }.Where(control => !Fits(control, card)).Select(control => control.ToString() ?? string.Empty).ToList(),
                    item.Bounds.Width);
                box.IsDropDownOpen = false;
                return result;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(layout.NameWidth >= 60, $"The name is {layout.NameWidth:F0} px wide.");
        Assert.False(layout.IdCut);
        Assert.Equal([], layout.Outside);
        Assert.True(layout.ItemWidth <= 400, $"The release list is {layout.ItemWidth:F0} px wide.");
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task ReleaseList_MarkOfTheSelectedRelease_IsReadable(string theme)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: MarkedMeasureTools);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        LoadMarkedPack(editor);

        var (danger, behind) = await HeadlessApp.RunAsync(harness, async () =>
        {
            var before = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 1080, Content = page, DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var box = page.GetVisualDescendants().OfType<ComboBox>().Single(box => box.DataContext is ListingPackMemberRow { Id: "MeasureTools" });
                box.IsDropDownOpen = true;
                await HeadlessApp.FramesAsync(5);
                var item = (ComboBoxItem)box.ContainerFromIndex(0)!;
                Assert.True(item.IsSelected);
                var mark = item.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("danger"));
                var host = TopLevel.GetTopLevel(mark)!;
                using var frame = host.CaptureRenderedFrame()!;
                using var buffer = frame.Lock();
                // a point just left of the text, where no glyph is drawn
                var point = mark.TranslatePoint(new Point(-3, mark.Bounds.Height / 2), host)!.Value;
                var colors = (UnexpectedErrorBarTests.Token(mark, "Color.Danger"), UnexpectedErrorBarTests.Pixel(buffer, point));
                box.IsDropDownOpen = false;
                return colors;
            }
            finally
            {
                window.Close();
                Application.Current.RequestedThemeVariant = before;
            }
        });

        var contrast = UnexpectedErrorBarTests.Contrast(danger, behind);
        Assert.True(contrast >= UnexpectedErrorBarTests.ReadableContrast, $"The mark has a contrast of {contrast:F2} on {behind} in the {theme} theme.");
    }

    /// <summary>MeasureTools 1.1.10 needs a mod and one of two mods that are not listed, so its mark is long.</summary>
    private static string MarkedMeasureTools(string json)
    {
        var root = JsonNode.Parse(json)!;
        var release = root["listings"]!.AsArray().Single(listing => (string?)listing!["id"] == "MeasureTools")!["releases"]!.AsArray()
            .Single(release => (string?)release!["version"] == "1.1.10")!;
        var dependencies = (release["dependencies"] ??= new JsonArray()).AsArray();
        dependencies.Add(new JsonObject { ["id"] = "KittenExtensions", ["kind"] = "required" });
        dependencies.Add(new JsonObject { ["any_of"] = new JsonArray(new JsonObject { ["id"] = "ShaderExtensions" }, new JsonObject { ["id"] = "KittenExtensionsContinued" }), ["kind"] = "required" });
        return root.ToJsonString();
    }

    private static void LoadMarkedPack(ListingEditor editor) => editor.Load(new ListingDraft
    {
        Type = ListingDraft.ModPackType,
        Id = "my-pack",
        Version = "1.0.0",
        Mods = [new ListingPackMember("AdvancedFlightComputer", "0.7.5"), new ListingPackMember("MeasureTools", "1.1.10")],
    });

    private sealed record MemberRowLayout(double NameWidth, bool IdCut, List<string> Outside, double ItemWidth);

    private static void FillValidListing(ListingEditor editor)
    {
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";
    }

    private static HttpResponseMessage? ServeStarMap(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/listings/StarMap.toml", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListingEditorTests.StarMapListing) }
            : null;

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static bool Fits(Control control, Border card)
    {
        var corner = control.TranslatePoint(new Point(0, 0), card)
            ?? throw new InvalidOperationException($"{control} is not laid out inside the card.");

        return corner.X >= card.Padding.Left
            && corner.Y >= card.Padding.Top
            && corner.X + control.Bounds.Width <= card.Bounds.Width - card.Padding.Right
            && corner.Y + control.Bounds.Height <= card.Bounds.Height - card.Padding.Bottom;
    }

    private sealed record SearchLayout(int Rows, int OwnChips, List<string> Outside, bool Trimmed);

    /// <summary>The visible texts, with the Markdown of every visible Markdown view.</summary>
    private static Task<List<string?>> RenderAsync(ViewModelHarness harness) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)
                .Concat(page.GetVisualDescendants().OfType<MarkdownView>().Where(view => view.IsEffectivelyVisible).Select(view => view.Markdown))
                .ToList();
            window.Close();
            return Task.FromResult(texts);
        });
}
