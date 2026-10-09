using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Listings;

namespace Borea.App.Tests.ViewModels;

public sealed class ListingDependencyHelpTests
{
    internal const string AdvancedFlightComputerListing = """
        spec_version = 1
        id = "AdvancedFlightComputer"
        type = "mod"
        name = "Advanced Flight Computer"
        authors = ["Maxi"]
        abstract = "Flies the burns for you."
        license = "MIT"
        tags = ["gameplay"]

        [releases]
        github = "Maximilian-Nesslauer/KSA-AdvancedFlightComputer"

        [links]
        forums = "https://forums.ahwoo.com/threads/advanced-flight-computer.783/"

        [compatibility]
        game_min = "2026.8.3.5117"

        [loader]
        id = "StarMap"
        min = "0.4.6"
        """;

    private const string ModToml = """
        name = "MyMod"

        [[StarMap.ModDependencies]]
        ModId = "MeasureTools"

        [[StarMap.ModDependencies]]
        ModId = "Extra"
        Optional = true
        """;

    [Fact]
    public async Task ListedMod_ShowsTheDerivedDependenciesOfItsNewestRelease_AndAddBoundsAddsAnEntry()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeAdvancedFlightComputer);
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "advanced";
        await editor.LoadListedCommand.ExecuteAsync(null);

        var declared = Assert.Single(editor.DeclaredDependencies);
        Assert.Equal(localization.FormatListingDeclared("0.7.5"), editor.DeclaredText);
        Assert.Equal(localization.FormatListingDeclaredOptional("KittenExtensions"), declared.Text);
        Assert.True(declared.CanAddBounds);
        Assert.Empty(editor.Dependencies);
        Assert.DoesNotContain("[[dependencies]]", editor.DocumentText, StringComparison.Ordinal);

        declared.AddBoundsCommand.Execute(null);

        var row = Assert.Single(editor.Dependencies);
        Assert.True(row.IsEditable);
        Assert.Equal(("KittenExtensions", "optional"), (row.Id, row.Kind));
        Assert.False(declared.CanAddBounds);
        Assert.Contains("[[dependencies]]\nid = \"KittenExtensions\"\nkind = \"optional\"\n", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFromMyArchive_NewListing_ShowsTheModTomlDependencies_AndDownloadsNothingBeforeTheClick()
    {
        var downloads = 0;
        var serve = ServeRelease(Archive(ModToml));
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
                downloads++;
            return serve(request);
        });
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.ReleasesGitHub = "owner/KSA-MyMod";

        Assert.Empty(editor.DeclaredDependencies);
        Assert.Null(editor.DeclaredText);
        Assert.Equal(0, downloads);

        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.Equal(1, downloads);
        Assert.Null(editor.ArchiveReadText);
        Assert.Equal(localization.FormatListingDeclared("v1.0.0"), editor.DeclaredText);
        Assert.Equal(
            [localization.FormatListingDeclaredRequired("MeasureTools"), localization.FormatListingDeclaredOptional("Extra")],
            editor.DeclaredDependencies.Select(row => row.Text));
        Assert.Empty(editor.Dependencies);
    }

    [Fact]
    public async Task ReadFromMyArchive_WithoutAReleaseHost_SaysWhatItNeeds()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ListingDeclaredNeedsHost, editor.ArchiveReadText);
    }

    [Fact]
    public async Task ReadSource_ShowsWhatTheModTomlOfTheReadArchiveDeclares()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeRelease(Archive(ModToml)));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.SourceText = "owner/KSA-MyMod";

        await editor.ReadSourceCommand.ExecuteAsync(null);

        Assert.Equal(["MeasureTools", "Extra"], editor.DeclaredDependencies.Select(row => row.Dependency.Id));
    }

    [Fact]
    public async Task IdSearch_FindsAListedModByName_AndAnUnlistedIdGetsTheWarning()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";

        editor.DependencyQuery = "flight computer";

        var match = Assert.Single(editor.DependencyMatches);
        Assert.Equal("AdvancedFlightComputer", match.Id);
        Assert.Same(match, editor.SelectedDependencyMatch);

        editor.AddListedDependencyCommand.Execute(null);

        var listed = Assert.Single(editor.Dependencies);
        Assert.Equal(("AdvancedFlightComputer", "required"), (listed.Id, listed.Kind));
        Assert.Equal(localization.ListingDependencyKindRequired, listed.KindText);
        Assert.Null(listed.NoteText);
        Assert.Equal(string.Empty, editor.DependencyQuery);

        editor.DependencyQuery = "advanced";
        Assert.Empty(editor.DependencyMatches);
        Assert.True(editor.HasNoDependencyMatch);

        editor.AddDependencyCommand.Execute(null);
        var unlisted = editor.Dependencies[1];
        unlisted.Id = "NotListedMod";

        Assert.Equal(localization.FormatListingDependencyNotListed("NotListedMod"), unlisted.NoteText);
        Assert.Contains(editor.Notes, issue => issue.Location == "dependencies[1]" && issue.Message == unlisted.NoteText);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location.StartsWith("dependencies", StringComparison.Ordinal));
        Assert.Contains("id = \"NotListedMod\"", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versions_AreTheStampedReleasesNewestFirst_WithoutTheYankedOnes_AndTakeFreeText()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: YankAndMarkDev);
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.AddDependencyCommand.Execute(null);
        var row = editor.Dependencies[0];

        row.Id = "MeasureTools";

        Assert.Equal(
            [new ListingReleaseChoice("1.1.9", localization.ReleaseDev), new ListingReleaseChoice("1.1.8", ""), new ListingReleaseChoice("1.1.7", "")],
            row.Versions);

        row.NeedsNewestCommand.Execute(null);

        Assert.Equal("1.1.8", row.Min);
        Assert.True(row.CanNeedNewest);

        row.Min = "0.5";

        Assert.Contains("[[dependencies]]\nid = \"MeasureTools\"\nkind = \"required\"\nmin = \"0.5\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Null(row.ErrorText);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location.StartsWith("dependencies", StringComparison.Ordinal));

        row.Kind = "conflict";

        Assert.False(row.CanNeedNewest);
    }

    [Fact]
    public async Task Mistakes_AreNamedOnTheirEntry_WithTheAnswersOfTheChecks()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.Load(new ListingDraft
        {
            Id = "MyMod",
            Dependencies =
            [
                new ListingDependency("MeasureTools", "required", "1.1", "1.0.9"),
                new ListingDependency("mymod", "required"),
                new ListingDependency("measuretools", "conflict"),
                new ListingDependency("AdvancedFlightComputer", "needs"),
            ],
        });

        var rows = editor.Dependencies;
        Assert.Equal("max '1.0.9' is below min '1.1'", rows[0].ErrorText);
        Assert.Equal("a listing cannot depend on itself", rows[1].ErrorText);
        Assert.Null(rows[1].NoteText);
        Assert.Contains("'measuretools' already has a dependency entry", rows[2].ErrorText, StringComparison.Ordinal);
        Assert.Equal("'needs' is not one of ['required', 'optional', 'recommends', 'suggests', 'conflict']", rows[3].ErrorText);
        Assert.Equal("needs", rows[3].KindChoices[0]);
        Assert.Null(rows[3].KindText);
        Assert.False(rows[3].HasKindText);
        Assert.Equal(localization.ListingDependencyKindConflict, rows[2].KindText);

        rows[3].Kind = "suggests";

        Assert.Null(rows[3].ErrorText);
        Assert.Equal(localization.ListingDependencyKindSuggests, rows[3].KindText);
    }

    [Fact]
    public async Task AddBounds_OnTheLoader_SetsTheLoader_AndOnAModUsesTheSpellingOfTheIndex()
    {
        const string modToml = """
            [[StarMap.ModDependencies]]
            ModId = "StarMap"

            [[StarMap.ModDependencies]]
            ModId = "measuretools"
            """;
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeRelease(Archive(modToml)));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.ReleasesGitHub = "owner/KSA-MyMod";
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);
        var starMap = editor.DeclaredDependencies[0];
        var measureTools = editor.DeclaredDependencies[1];
        Assert.True(starMap.IsLoader);
        Assert.False(measureTools.IsLoader);

        starMap.AddBoundsCommand.Execute(null);

        Assert.Empty(editor.Dependencies);
        Assert.True(editor.UsesLoader);
        Assert.Equal(("StarMap", "0.4.6"), (editor.LoaderId, editor.LoaderMin));
        Assert.Equal(harness.Localization.FormatListingLoaderSet("StarMap"), editor.LoaderSetText);
        Assert.False(starMap.CanAddBounds);

        measureTools.AddBoundsCommand.Execute(null);

        var row = Assert.Single(editor.Dependencies);
        Assert.Equal(("MeasureTools", "required"), (row.Id, row.Kind));
        Assert.DoesNotContain(editor.Errors, issue => issue.Location.StartsWith("dependencies", StringComparison.Ordinal) || issue.Location == "loader");
    }

    [Fact]
    public async Task IdSearch_MarksALoader_AndPickingItSetsTheLoader()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";

        editor.DependencyQuery = "starmap";

        var match = Assert.Single(editor.DependencyMatches);
        Assert.True(match.IsLoader);

        editor.AddListedDependencyCommand.Execute(null);

        Assert.Empty(editor.Dependencies);
        Assert.True(editor.UsesLoader);
        Assert.Equal("StarMap", editor.LoaderId);
        editor.DependencyQuery = "starmap";
        Assert.Empty(editor.DependencyMatches);
    }

    [Fact]
    public async Task KeptEntry_NamesItsAlternatives_SoAddBoundsAndTheSearchLeaveThemOut()
    {
        const string listing = """
            spec_version = 1
            id = "MyMod"
            type = "mod"
            name = "My Mod"
            authors = ["Maxi"]
            abstract = "Does a thing."
            license = "MIT"

            [releases]
            github = "owner/KSA-MyMod"

            [links]
            forums = "https://forums.ahwoo.com/threads/my-mod.42/"

            [compatibility]
            game_min = "2026.9.7.5402"

            [[dependencies]]
            any_of = [{ id = "MeasureTools" }, { id = "KSArmory" }]
            kind = "required"
            """;
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeRelease(Archive(ModToml)));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.Load(ListingDraft.FromDocument(harness.Services.ListingFormat.Read(listing)));
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        var measureTools = editor.DeclaredDependencies.Single(row => row.Dependency.Id == "MeasureTools");
        Assert.False(measureTools.CanAddBounds);
        Assert.True(editor.DeclaredDependencies.Single(row => row.Dependency.Id == "Extra").CanAddBounds);

        editor.DependencyQuery = "measure";

        Assert.Empty(editor.DependencyMatches);
    }

    [Fact]
    public async Task ReadFromMyArchive_ReadsTheModTomlOfTheAuthoredInstallRoot()
    {
        const string listing = """
            spec_version = 1
            id = "MyMod"
            type = "mod"
            name = "My Mod"
            authors = ["Maxi"]
            abstract = "Does a thing."
            license = "MIT"

            [releases]
            github = "owner/KSA-MyMod"

            [install]
            root = "GameData/MyMod"

            [links]
            forums = "https://forums.ahwoo.com/threads/my-mod.42/"

            [compatibility]
            game_min = "2026.9.7.5402"
            """;
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeRelease(Archive(("GameData/MyMod/mod.toml", ModToml), ("Docs/readme.txt", "text"))));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.Load(ListingDraft.FromDocument(harness.Services.ListingFormat.Read(listing)));

        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.Null(editor.ArchiveReadText);
        Assert.Equal(["MeasureTools", "Extra"], editor.DeclaredDependencies.Select(row => row.Dependency.Id));
    }

    [Fact]
    public async Task ReadFromMyArchive_ModTomlAtTheArchiveRoot_SaysThatTheIndexFindsNoInstallRoot()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeRelease(Archive(("mod.toml", ModToml), ("MyMod.dll", "binary"))));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.ReleasesGitHub = "owner/KSA-MyMod";

        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatListingDeclaredNoRoot("v1.0.0"), editor.ArchiveReadText);
        Assert.Empty(editor.DeclaredDependencies);
        Assert.Null(editor.DeclaredText);
        Assert.Contains(editor.Errors, issue => issue.Location == "id" && issue.Message.StartsWith("the install root is neither derivable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangingTheReleaseHost_ForgetsWhatTheArchiveDeclared()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeRelease(Archive(ModToml)));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.ReleasesGitHub = "owner/KSA-MyMod";
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);
        Assert.NotEmpty(editor.DeclaredDependencies);

        editor.ReleasesGitHub = "owner/KSA-Other";

        Assert.Empty(editor.DeclaredDependencies);
        Assert.Null(editor.DeclaredText);
        Assert.Null(editor.ArchiveReadText);
    }

    [Fact]
    public async Task ModLoaderListing_HasNoDeclaredHelp_BecauseTheIndexReadsNoModTomlForIt()
    {
        var downloads = 0;
        var serve = ServeRelease(Archive(ModToml));
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
                downloads++;
            return serve(request);
        });
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.Load(new ListingDraft { Id = "MyLoader", Type = ListingDraft.ModLoaderType, Releases = new ListingReleases("owner/KSA-MyMod", null) });

        Assert.False(editor.HasDeclaredHelp);

        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.Equal(0, downloads);
        Assert.Empty(editor.DeclaredDependencies);
        editor.DependencyQuery = "starmap";
        Assert.Empty(editor.DependencyMatches);
    }

    /// <summary>Yanks MeasureTools 1.1.10 and marks 1.1.9 as a dev build.</summary>
    private static string YankAndMarkDev(string json)
    {
        var root = JsonNode.Parse(json)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "MeasureTools")!;
        foreach (var release in listing["releases"]!.AsArray())
        {
            if ((string?)release!["version"] == "1.1.10")
                release["yanked"] = true;
            if ((string?)release["version"] == "1.1.9")
                release["release_status"] = "dev";
        }

        return root.ToJsonString();
    }

    private static HttpResponseMessage? ServeAdvancedFlightComputer(HttpRequestMessage request) =>
        request.RequestUri!.AbsoluteUri == "https://raw.githubusercontent.com/KSAModding/content-index/main/listings/AdvancedFlightComputer.toml"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AdvancedFlightComputerListing) }
            : null;

    /// <summary>The GitHub repository owner/KSA-MyMod with one release v1.0.0 that carries the archive.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> ServeRelease(byte[] archive) => request => request.RequestUri!.AbsoluteUri switch
    {
        "https://api.github.com/repos/owner/KSA-MyMod" => Json("""
            {
              "name": "KSA-MyMod", "full_name": "owner/KSA-MyMod", "html_url": "https://github.com/owner/KSA-MyMod",
              "description": "Does a thing.", "has_issues": true, "owner": { "login": "owner", "type": "User" }, "license": { "spdx_id": "MIT" }
            }
            """),
        "https://api.github.com/repos/owner/KSA-MyMod/releases?per_page=100" => Json($$"""
            [ { "tag_name": "v1.0.0", "draft": false, "published_at": "2026-09-08T00:00:00Z", "assets": [
              { "name": "MyMod.zip", "state": "uploaded", "size": {{archive.Length}}, "browser_download_url": "https://github.com/owner/KSA-MyMod/releases/download/v1.0.0/MyMod.zip" }
            ] } ]
            """),
        "https://github.com/owner/KSA-MyMod/releases/download/v1.0.0/MyMod.zip" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) },
        _ => null,
    };

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static byte[] Archive(string modToml) => Archive(("MyMod/mod.toml", modToml), ("MyMod/MyMod.dll", "binary"));

    private static byte[] Archive(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
