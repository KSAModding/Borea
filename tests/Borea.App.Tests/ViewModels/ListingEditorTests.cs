using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Text;
using Borea.App.ViewModels;
using Borea.Core.Listings;

namespace Borea.App.Tests.ViewModels;

public sealed class ListingEditorTests
{
    private const string Repository = """
        {
          "name": "KSA-MyMod", "full_name": "owner/KSA-MyMod", "html_url": "https://github.com/owner/KSA-MyMod",
          "description": "Does a thing.", "homepage": "https://forums.ahwoo.com/threads/my-mod.42/", "has_issues": true,
          "owner": { "login": "owner", "type": "User" }, "license": { "spdx_id": "MIT" }
        }
        """;

    private static readonly string Releases = $$"""
        [ { "tag_name": "v1.0.0", "draft": false, "published_at": "2026-09-08T00:00:00Z", "assets": [
          { "name": "MyMod.zip", "state": "uploaded", "size": {{Archive().Length}}, "browser_download_url": "https://github.com/owner/KSA-MyMod/releases/download/v1.0.0/MyMod.zip" }
        ] } ]
        """;

    internal const string StarMapListing = """
        spec_version = 1
        id = "StarMap"
        type = "mod-loader"
        name = "StarMap"
        authors = ["KlaasWhite"]
        abstract = "Mod loader that runs code mods for Kitten Space Agency."
        license = "MIT"
        tags = ["library"]

        [releases]
        github = "StarMapLoader/StarMap"

        [links]
        forums = "https://forums.ahwoo.com/threads/starmap-mod-loader.384/"

        [compatibility]
        game_min = "2026.8.3.5117"

        [install]
        target = "standalone"

        [provides]
        launch = "StarMap.exe"
        content-dir = "mods"
        """;

    [Fact]
    public async Task OpenListing_ShowsTheStartStepWithTheListedMods()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscover();

        await viewModel.OpenListingAsync();

        Assert.True(viewModel.CurrentWindowListing);
        Assert.False(viewModel.CurrentWindowDiscover);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.True(viewModel.ListingEditor.IsStartStep);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools", "StarMap"], viewModel.ListingEditor.ListedMatches.Select(listing => listing.Id));

        viewModel.SetMainWindowHome();

        Assert.False(viewModel.CurrentWindowListing);
        Assert.False(viewModel.IsDiscoverSection);
    }

    [Fact]
    public async Task ReadSource_GitHubRepository_FillsTheFormFromTheHostTheArchiveAndTheForums()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), editSnapshot: WithGameplayPrefix);
        var editor = harness.ViewModel.ListingEditor;
        var opening = harness.ViewModel.OpenListingAsync();
        editor.SourceText = "https://github.com/owner/KSA-MyMod";

        await editor.ReadSourceCommand.ExecuteAsync(null);
        await opening;

        Assert.Null(editor.SourceError);
        Assert.True(editor.IsFormStep);
        Assert.False(editor.IsEdit);
        Assert.Equal("MyMod", editor.Id);
        Assert.Equal("KSA-MyMod", editor.Name);
        Assert.Equal("owner", editor.Authors);
        Assert.Equal("MIT", editor.License);
        Assert.Equal("https://forums.ahwoo.com/threads/my-mod.42/", editor.Forums);
        Assert.Equal("owner/KSA-MyMod", editor.ReleasesGitHub);
        Assert.True(editor.UsesLoader);
        Assert.Equal("0.4.6", editor.LoaderMin);
        Assert.Equal("2026.9.7.5402", editor.GameMin);
        Assert.True(editor.CuratedTags.Single(chip => chip.Tag == "gameplay").IsSelected);
        Assert.Contains("MyMod", editor.ArchiveText);
        Assert.Contains("id = \"MyMod\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("[loader]\nid = \"StarMap\"\nmin = \"0.4.6\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));
        Assert.True(editor.CanOpenPullRequest);
    }

    [Fact]
    public async Task ReadSource_TextThatNamesNoHost_SaysSo()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.SourceText = "not a source";

        await editor.ReadSourceCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ListingSourceInvalid, editor.SourceError);
        Assert.True(editor.IsStartStep);
    }

    [Fact]
    public async Task ReadSource_UnknownRepository_StaysOnTheStartStep()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var editor = harness.ViewModel.ListingEditor;
        editor.SourceText = "owner/gone";

        await editor.ReadSourceCommand.ExecuteAsync(null);

        Assert.Contains("owner/gone", editor.SourceError);
        Assert.True(editor.IsStartStep);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadSource_CancelledOrPageLeft_StopsWithoutLoadingOrError(bool leavePage)
    {
        ViewModelHarness? owner = null;
        var serve = Serve();
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
            {
                if (leavePage)
                    owner!.ViewModel.SetMainWindowHome();
                else
                    owner!.ViewModel.ListingEditor.CancelCommand.Execute(null);
            }

            return serve(request);
        });
        owner = harness;
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.SourceText = "owner/KSA-MyMod";

        await editor.ReadSourceCommand.ExecuteAsync(null);

        Assert.True(editor.IsStartStep);
        Assert.Null(editor.SourceError);
        Assert.False(editor.IsBusy);
        Assert.Equal(string.Empty, editor.Id);
    }

    [Fact]
    public async Task Forums_TypedThreadLink_SelectsTheTagsOfItsPrefix()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), editSnapshot: WithGameplayPrefix);
        var editor = harness.ViewModel.ListingEditor;
        editor.ForumsDelay = TimeSpan.Zero;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        await editor.TagProposal;

        Assert.True(editor.CuratedTags.Single(chip => chip.Tag == "gameplay").IsSelected);
        Assert.Contains("tags = [\"gameplay\"]\n", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forums_TagAlreadyChosen_IsLeftAsItIs()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), editSnapshot: WithGameplayPrefix);
        var editor = harness.ViewModel.ListingEditor;
        editor.ForumsDelay = TimeSpan.Zero;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.CuratedTags.Single(chip => chip.Tag == "library").IsSelected = true;

        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        await editor.TagProposal;

        Assert.Equal(["library"], editor.CuratedTags.Where(chip => chip.IsSelected).Select(chip => chip.Tag));
    }

    [Fact]
    public async Task Fields_ChecksFollowWhatTheAuthorTypes()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        Assert.True(editor.HasErrors);
        Assert.False(editor.CanOpenPullRequest);

        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";

        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));
        Assert.True(editor.CanOpenPullRequest);

        editor.License = "MIT, GPL";

        Assert.Contains(editor.Errors, issue => issue.Location == "license");
        Assert.False(editor.CanOpenPullRequest);

        editor.License = "MIT";
        editor.ReleasesSpaceDock = "abc";

        Assert.Contains(editor.Errors, issue => issue.Location == "releases.spacedock" && issue.Message == "'abc' is not a number.");
    }

    [Fact]
    public async Task Overview_NewForm_ListsWhatIsMissingInsteadOfErrors()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        Assert.True(editor.HasErrors);
        Assert.Empty(editor.VisibleErrors);
        Assert.Contains(localization.ListingName, editor.MissingText);
        Assert.Contains(localization.LinkForum, editor.MissingText);
        var steps = editor.Steps.ToDictionary(step => step.Key);
        Assert.Equal(ListingStepState.ToDo, steps["about"].State);
        Assert.Equal(ListingStepState.ToDo, steps["links"].State);
        Assert.Equal(localization.ListingStepRecommended, steps["releases"].Detail);
        Assert.Equal(ListingStepState.Optional, steps["dependencies"].State);
        Assert.Equal(localization.ListingPreviewName, editor.PreviewName);
    }

    [Fact]
    public async Task Overview_EmptyRowsAreMissing_AndARealErrorStillShowsWithItsName()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.AddIconCommand.Execute(null);
        editor.AddDependencyCommand.Execute(null);

        Assert.Contains(localization.ListingIcon, editor.MissingText);
        Assert.Contains(localization.FormatListingDependencyNumber(1), editor.MissingText);
        Assert.Empty(editor.VisibleErrors);

        editor.License = "MIT, GPL";

        var error = Assert.Single(editor.VisibleErrors);
        Assert.StartsWith(localization.ListingLicense + ": ", error, StringComparison.Ordinal);
        Assert.Equal(ListingStepState.Fix, editor.Steps.Single(step => step.Key == "about").State);
    }

    [Fact]
    public async Task Overview_FilledSections_AreDone()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";

        Assert.Null(editor.MissingText);
        Assert.Empty(editor.VisibleErrors);
        var steps = editor.Steps.ToDictionary(step => step.Key);
        Assert.Equal(ListingStepState.Done, steps["about"].State);
        Assert.Equal(ListingStepState.Done, steps["links"].State);
        Assert.Equal(ListingStepState.Done, steps["releases"].State);
        Assert.Equal("My Mod", editor.PreviewName);
        Assert.Equal(harness.Localization.FormatContentByAuthor("Maxi"), editor.PreviewAuthors);
    }

    [Fact]
    public async Task OpenPullRequest_NewListing_OpensTheNewFilePageWithTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidNewListingAsync(harness);

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        var url = Assert.Single(opened);
        Assert.StartsWith("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml&value=spec_version%20%3D%201%0A", url, StringComparison.Ordinal);
        Assert.True(editor.LastPullRequestPage!.CarriesText);
        Assert.Null(window.CopiedText);
        Assert.Equal(harness.Localization.ListingOpenedWithText, editor.OutputMessage);
    }

    [Fact]
    public async Task OpenPullRequest_FileTooLongForTheUrl_OpensThePageAndCopiesTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidNewListingAsync(harness);
        editor.Description = string.Concat(Enumerable.Repeat("A long description line.\n", 400));

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml", Assert.Single(opened));
        Assert.False(editor.LastPullRequestPage!.CarriesText);
        Assert.Equal(editor.DocumentText, window.CopiedText);
        Assert.Equal(harness.Localization.ListingOpenedPaste, editor.OutputMessage);
    }

    [Fact]
    public async Task OpenPullRequest_PageWithTheFileDoesNotOpen_OpensItEmptyAndCopiesTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidNewListingAsync(harness);
        harness.ViewModel.OpenWithSystem = url =>
        {
            if (url.Contains("&value=", StringComparison.Ordinal))
                throw new Win32Exception("The URL is too long.");
            opened.Add(url);
        };

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml", Assert.Single(opened));
        Assert.Equal(editor.DocumentText, window.CopiedText);
        Assert.Equal(harness.Localization.ListingOpenedPaste, editor.OutputMessage);
    }

    [Fact]
    public async Task OpenPullRequest_NoBrowser_SaysWhyWithoutTheUrl()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        harness.ViewModel.OpenWithSystem = _ => throw new Win32Exception("No browser.");

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatListingOpenFailed("No browser."), editor.OutputMessage);
        Assert.Equal(editor.DocumentText, window.CopiedText);
    }

    [Fact]
    public async Task LoadListed_Loader_KeepsItsTablesAndOpensTheEditPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
            request.RequestUri!.AbsoluteUri == "https://raw.githubusercontent.com/KSAModding/content-index/main/listings/StarMap.toml"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StarMapListing) }
                : null);
        var editor = harness.ViewModel.ListingEditor;
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var window = new FakeWindowServices();
        harness.ViewModel.WindowServices = window;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "starm";

        await editor.LoadListedCommand.ExecuteAsync(null);
        editor.Abstract = "Runs code mods.";
        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.True(editor.IsEdit);
        Assert.False(editor.CanUseLoader);
        Assert.EndsWith("[install]\ntarget = \"standalone\"\n\n[provides]\nlaunch = \"StarMap.exe\"\ncontent-dir = \"mods\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("abstract = \"Runs code mods.\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal("https://github.com/KSAModding/content-index/edit/main/listings/StarMap.toml", Assert.Single(opened));
        Assert.Equal(editor.DocumentText, window.CopiedText);
    }

    [Theory]
    [InlineData("flight com", "AdvancedFlightComputer")]
    [InlineData("MEASURE", "MeasureTools")]
    [InlineData("ksarm", "KSArmory")]
    public async Task ListedQuery_FindsPartOfTheNameOrTheIdInAnyCaseAndChoosesIt(string query, string id)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.ListedQuery = query;

        Assert.Equal(id, Assert.Single(editor.ListedMatches).Id);
        Assert.Equal(id, editor.SelectedListed?.Id);
        Assert.True(editor.LoadListedCommand.CanExecute(null));
    }

    [Fact]
    public async Task ListedQuery_NoMatch_SaysSoAndLoadsNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.ListedQuery = "nothing like it";

        Assert.Empty(editor.ListedMatches);
        Assert.True(editor.HasNoListedMatch);
        Assert.Null(editor.SelectedListed);
        Assert.False(editor.LoadListedCommand.CanExecute(null));
    }

    [Fact]
    public async Task MoveListedSelection_StepsThroughTheMatchesAndStopsAtTheEnds()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "s";

        editor.MoveListedSelection(1);
        editor.MoveListedSelection(1);
        editor.MoveListedSelection(1);
        var last = editor.SelectedListed?.Id;
        editor.MoveListedSelection(-1);

        Assert.Equal(["KSArmory", "MeasureTools", "StarMap"], editor.ListedMatches.Select(listing => listing.Id));
        Assert.Equal("StarMap", last);
        Assert.Equal("MeasureTools", editor.SelectedListed?.Id);
    }

    [Fact]
    public async Task SignedIn_OwnListingsComeFirstUntilTheSignOut()
    {
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, editSnapshot: json => json
            .Replace("StarMapLoader/StarMap", "OctoCat/StarMap", StringComparison.Ordinal)
            .Replace("LaurensDeV/KSArmory", "octocat-org/KSArmory", StringComparison.Ordinal));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        var signedIn = editor.ListedMatches.ToList();
        editor.SelectedListed = signedIn[0];
        session.SignOut();

        Assert.Equal(
            [("StarMap", true), ("AdvancedFlightComputer", false), ("KSArmory", false), ("MeasureTools", false)],
            signedIn.Select(listing => (listing.Id, listing.IsOwn)));
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools", "StarMap"], editor.ListedMatches.Select(listing => listing.Id));
        Assert.DoesNotContain(editor.ListedMatches, listing => listing.IsOwn);
        Assert.Same(editor.ListedMatches[3], editor.SelectedListed);
    }

    [Fact]
    public async Task ListedSearch_FindsAnAuthorAndNamesTheAuthors()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.ListedQuery = "laurens";

        var match = Assert.Single(editor.ListedMatches);
        Assert.Equal("KSArmory", match.Id);
        Assert.Equal(harness.ViewModel.Localization.FormatContentByAuthor("Laurens"), match.AuthorsText);

        // the "by" around the names is display text, not something a listing is found by
        editor.ListedQuery = "by";
        Assert.Empty(editor.ListedMatches);
    }

    [Fact]
    public async Task LeavingThePage_KeepsTheDraft()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.OpenListingAsync();
        viewModel.ListingEditor.StartEmptyCommand.Execute(null);
        viewModel.ListingEditor.Name = "Kept";

        viewModel.SetMainWindowLibrary();
        await viewModel.OpenListingAsync();

        Assert.True(viewModel.ListingEditor.IsFormStep);
        Assert.Equal("Kept", viewModel.ListingEditor.Name);
        Assert.Equal("Kept", viewModel.ListingEditor.Draft.Name);
    }

    [Fact]
    public async Task ChooseFile_MeasuresTheImageForItsRecord()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        window.ImageToOpen = new PickedBinaryFile("icon.png", Png(512, 512));
        editor.AddIconCommand.Execute(null);
        var icon = editor.Icon!;
        icon.Url = "https://example.com/icon.png";

        await icon.ChooseFileCommand.ExecuteAsync(null);

        Assert.True(icon.IsMeasured);
        Assert.Null(icon.Problem);
        Assert.Contains("[images.icon]\nurl = \"https://example.com/icon.png\"\nsha256 = \"", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("width = 512\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));
    }

    [Fact]
    public async Task ChooseFile_ImageOutsideTheLimits_SaysWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        window.ImageToOpen = new PickedBinaryFile("small.png", Png(100, 100));
        editor.AddIconCommand.Execute(null);

        await editor.Icon!.ChooseFileCommand.ExecuteAsync(null);

        Assert.False(editor.Icon.IsMeasured);
        Assert.Contains("outside the limits", editor.Icon.Problem);
    }

    [Fact]
    public async Task ChooseFile_FailsAfterAMeasuredImage_DropsTheOldFacts()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        editor.AddIconCommand.Execute(null);
        var icon = editor.Icon!;
        icon.Url = "https://example.com/icon.png";
        window.ImageToOpen = new PickedBinaryFile("icon.png", Png(512, 512));
        await icon.ChooseFileCommand.ExecuteAsync(null);

        window.ImageToOpen = new PickedBinaryFile("small.png", Png(100, 100));
        await icon.ChooseFileCommand.ExecuteAsync(null);

        Assert.False(icon.IsMeasured);
        Assert.Null(icon.Width);
        Assert.Null(icon.Size);
        Assert.DoesNotContain("sha256", editor.DocumentText, StringComparison.Ordinal);
    }

    private static async Task<(ListingEditor Editor, List<string> Opened, FakeWindowServices Window)> ValidNewListingAsync(ViewModelHarness harness)
    {
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var window = new FakeWindowServices();
        harness.ViewModel.WindowServices = window;
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";
        return (editor, opened, window);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage?> Serve() => request => request.RequestUri!.AbsoluteUri switch
    {
        "https://api.github.com/repos/owner/KSA-MyMod" => Json(Repository),
        "https://api.github.com/repos/owner/KSA-MyMod/releases?per_page=100" => Json(Releases),
        "https://github.com/owner/KSA-MyMod/releases/download/v1.0.0/MyMod.zip" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive()) },
        "https://forums.ahwoo.com/threads/my-mod.42/" => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<h1 class=\"p-title-value\"><span class=\"label label--orange\" dir=\"auto\">Gameplay</span>My Mod</h1>"),
        },
        _ => null,
    };

    private static string WithGameplayPrefix(string json) =>
        """{ "tags": { "spec_version": 1, "mod": [{ "tag": "gameplay", "name": "Gameplay", "meaning": "Mechanics.", "forum_prefix": "Gameplay" }, { "tag": "library", "name": "Library", "meaning": "Code." }] }, """
        + json.TrimStart()[1..];

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static byte[] Archive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in new[] { ("MyMod/mod.toml", "name = \"MyMod\"\n"), ("MyMod/MyMod.dll", "binary") })
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Png(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        List<byte> bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        foreach (var (kind, body) in new[] { ("IHDR", header), ("IDAT", new byte[] { 0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01 }), ("IEND", Array.Empty<byte>()) })
        {
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
            bytes.AddRange(length);
            bytes.AddRange(Encoding.ASCII.GetBytes(kind));
            bytes.AddRange(body);
            bytes.AddRange(new byte[4]);
        }

        return [.. bytes];
    }

    private sealed class FakeWindowServices : IWindowServices
    {
        public string? CopiedText { get; private set; }

        public PickedBinaryFile? ImageToOpen { get; set; }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(suggestedFileName);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes) => Task.FromResult(ImageToOpen);

        public Task CopyTextAsync(string text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }
    }
}
