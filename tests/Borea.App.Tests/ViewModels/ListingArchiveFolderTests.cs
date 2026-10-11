using System.IO.Compression;
using System.Net;
using System.Text;
using Borea.App.ViewModels;
using Borea.Core.Listings;

namespace Borea.App.Tests.ViewModels;

/// <summary>The folder in the release zip has to carry the id, as derive_root of the stamper in content-index-releases requires.</summary>
public sealed class ListingArchiveFolderTests
{
    private const string ListedWithInstallRoot = """
        spec_version = 1
        id = "AdvancedFlightComputer"
        type = "mod"
        name = "Advanced Flight Computer"
        authors = ["Maxi"]
        abstract = "Flies the burns for you."
        license = "MIT"
        tags = ["gameplay"]

        [releases]
        github = "owner/KSA-MyMod"

        [links]
        forums = "https://forums.ahwoo.com/threads/advanced-flight-computer.783/"

        [compatibility]
        game_min = "2026.8.3.5117"

        [install]
        root = "AdvancedFlightComputer-0.7.5"
        """;

    /// <summary>The zip that the release v1.0.0 of owner/KSA-MyMod carries, or null when it carries none yet.</summary>
    private byte[]? _zip;

    /// <summary>The names of the zips of the release, which all hold <see cref="_zip"/>.</summary>
    private string[] _zipNames = ["MyMod.zip"];

    /// <summary>Whether the download of a zip works, else the network fails it.</summary>
    private bool _downloadWorks = true;

    [Fact]
    public async Task ReadSource_FolderNamedAfterTheId_Passes()
    {
        _zip = Zip("MyMod");
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);

        Assert.Equal("MyMod", editor.Id);
        Assert.DoesNotContain(editor.Errors, IsArchiveIssue);
    }

    [Fact]
    public async Task ReadSource_FolderWithTheVersion_ProposesTheIdWithoutIt_AndIsAnErrorBeforeThePullRequest()
    {
        _zip = Zip("MyMod-1.0.0");
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);

        Assert.Equal("MyMod", editor.Id);
        Assert.Contains(editor.Errors, issue => issue.Location == "id"
            && issue.Message == "the archive's top-level directory is 'MyMod-1.0.0', which does not match the id 'MyMod'");
        Assert.False(editor.CanOpenPullRequest);
    }

    [Fact]
    public async Task ReadSource_FolderInOtherCasing_IsAnErrorBeforeThePullRequest()
    {
        _zip = Zip("mymod");
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);

        editor.Id = "MyMod";

        Assert.Contains(editor.Errors, issue => issue.Location == "id" && issue.Message ==
            "the archive's top-level directory is 'mymod' and the id is 'MyMod': the folder name is the identity the game sees, so the casing has to match");
        Assert.False(editor.CanOpenPullRequest);
    }

    [Fact]
    public async Task ReadSource_NoSingleFolder_IsAnErrorBeforeThePullRequest()
    {
        _zip = Archive(("mod.toml", "name = \"MyMod\"\n"), ("MyMod.dll", "binary"));
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);

        editor.Id = "MyMod";

        Assert.Contains(editor.Errors, issue => issue.Location == "id" && issue.Message ==
            "the install root is neither derivable from the archive nor authored: the standard layout is one top-level directory containing mod.toml, named 'MyMod'");
        Assert.False(editor.CanOpenPullRequest);
    }

    [Fact]
    public async Task ReadSource_ReleaseWithoutAZip_BlocksThePullRequest_UntilANewReadChecksTheFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);
        editor.Id = "MyMod";

        var issue = Assert.Single(editor.Errors, IsArchiveIssue);
        Assert.Equal("releases", issue.Location);
        Assert.Equal(harness.Localization.FormatListingArchiveNotChecked("the release v1.0.0 carries no zip archive"), issue.Message);
        Assert.False(editor.CanOpenPullRequest);

        _zip = Zip("MyMod-1.0.0");
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        issue = Assert.Single(editor.Errors, IsArchiveIssue);
        Assert.Equal("the archive's top-level directory is 'MyMod-1.0.0', which does not match the id 'MyMod'", issue.Message);

        _zip = Zip("MyMod");
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.DoesNotContain(editor.Errors, IsArchiveIssue);
    }

    [Fact]
    public async Task ReadFromMyArchive_SeveralZips_TakesTheOneNamedAfterTheId()
    {
        _zip = Zip("MyMod");
        _zipNames = ["MyMod-source.zip", "MyMod.zip"];
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);

        var issue = Assert.Single(editor.Errors, issue => issue.Location == "releases");
        Assert.Contains("carries 2 archives and none of them is named after the listing", issue.Message, StringComparison.Ordinal);
        Assert.False(editor.CanOpenPullRequest);

        editor.Id = "MyMod";
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.DoesNotContain(editor.Errors, IsArchiveIssue);
    }

    [Fact]
    public async Task ReadSource_FailedDownload_IsNoError()
    {
        _zip = Zip("MyMod");
        _downloadWorks = false;
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);
        editor.Id = "MyMod";

        Assert.DoesNotContain(editor.Errors, IsArchiveIssue);
        Assert.Contains("No source served the archive", editor.ArchiveText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFromMyArchive_FailedDownload_KeepsTheOlderRead()
    {
        _zip = Zip("MyMod-1.0.0");
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);

        _downloadWorks = false;
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        var issue = Assert.Single(editor.Errors, IsArchiveIssue);
        Assert.Equal("id", issue.Location);
    }

    [Fact]
    public async Task ReadFromMyArchive_ZipRemovedAfterTheSourceRead_BlocksThePullRequest()
    {
        _zip = Zip("MyMod");
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);

        _zip = null;
        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        var issue = Assert.Single(editor.Errors, IsArchiveIssue);
        Assert.Equal("releases", issue.Location);
    }

    [Fact]
    public async Task ReleasesHostChanged_TheSourceReadNoLongerCounts()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve);
        var editor = await ReadSourceAsync(harness);
        editor.Id = "MyMod";
        Assert.Contains(editor.Errors, issue => issue.Location == "releases" && issue.Message.StartsWith(NotChecked(harness), StringComparison.Ordinal));

        editor.ReleasesGitHub = "owner/Other";

        Assert.DoesNotContain(editor.Errors, issue => issue.Message.StartsWith(NotChecked(harness), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthoredInstallRootInTheZip_Passes()
    {
        _zip = Zip("AdvancedFlightComputer-0.7.5");
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
            request.RequestUri!.AbsoluteUri == "https://raw.githubusercontent.com/KSAModding/content-index/main/listings/AdvancedFlightComputer.toml"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListedWithInstallRoot) }
                : Serve(request));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "advanced";
        await editor.LoadListedCommand.ExecuteAsync(null);

        await editor.ReadArchiveDependenciesCommand.ExecuteAsync(null);

        Assert.Contains("root = \"AdvancedFlightComputer-0.7.5\"", editor.DocumentText, StringComparison.Ordinal);
        Assert.Null(editor.ArchiveReadText);
        Assert.DoesNotContain(editor.Errors, IsArchiveIssue);
    }

    private static bool IsArchiveIssue(ListingIssue issue) => issue.Location is "id" or "releases" && issue.Severity == ListingIssueSeverity.Error;

    /// <summary>The start of the error that the archive was not checked, which is the same for every reason.</summary>
    private static string NotChecked(ViewModelHarness harness)
    {
        var text = harness.Localization.FormatListingArchiveNotChecked("{0}");
        return text[..text.IndexOf("{0}", StringComparison.Ordinal)];
    }

    private static async Task<ListingEditor> ReadSourceAsync(ViewModelHarness harness)
    {
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.SourceText = "owner/KSA-MyMod";
        await editor.ReadSourceCommand.ExecuteAsync(null);
        return editor;
    }

    /// <summary>The GitHub repository owner/KSA-MyMod with one release v1.0.0, which carries the current zips.</summary>
    private HttpResponseMessage? Serve(HttpRequestMessage request)
    {
        var zip = _zip;
        const string download = "https://github.com/owner/KSA-MyMod/releases/download/v1.0.0/";
        var assets = zip is null
            ? string.Empty
            : string.Join(", ", _zipNames.Select(name => $$"""{ "name": "{{name}}", "state": "uploaded", "size": {{zip.Length}}, "browser_download_url": "{{download}}{{name}}" }"""));
        var url = request.RequestUri!.AbsoluteUri;
        if (zip is not null && _downloadWorks && url.StartsWith(download, StringComparison.Ordinal) && _zipNames.Contains(url[download.Length..]))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };

        return url switch
        {
            "https://api.github.com/repos/owner/KSA-MyMod" => Json("""
                {
                  "name": "KSA-MyMod", "full_name": "owner/KSA-MyMod", "html_url": "https://github.com/owner/KSA-MyMod",
                  "description": "Does a thing.", "has_issues": true, "owner": { "login": "owner", "type": "User" }, "license": { "spdx_id": "MIT" }
                }
                """),
            "https://api.github.com/repos/owner/KSA-MyMod/releases?per_page=100" =>
                Json($$"""[ { "tag_name": "v1.0.0", "draft": false, "published_at": "2026-09-08T00:00:00Z", "assets": [ {{assets}} ] } ]"""),
            _ => null,
        };
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static byte[] Zip(string folder) => Archive(($"{folder}/mod.toml", "name = \"MyMod\"\n"), ($"{folder}/MyMod.dll", "binary"));

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
