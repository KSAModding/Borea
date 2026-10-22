using System.Globalization;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ContentViewModelTests
{
    [Fact]
    public async Task Open_IndexMod_FillsHeaderLinksAndDetails()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");

        await afc.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.False(viewModel.CurrentWindowDiscover);
        Assert.Same(afc, viewModel.SelectedContent);
        Assert.False(viewModel.IsLoaderContent);
        Assert.True(viewModel.IsDescriptionTab);
        Assert.NotNull(viewModel.LatestVersion);
        Assert.StartsWith(">= ", afc.GameVersionText);
        Assert.Equal(harness.Localization.LinkForum, viewModel.ContentLinks[0].Label);
        Assert.Equal("forums", viewModel.ContentLinks[0].Key);
        Assert.Contains(viewModel.ContentLinks, link => link.Label == harness.Localization.LinkRepository);
        Assert.Contains(viewModel.ContentLinks, link => link.Label == "SpaceDock");
        Assert.True(viewModel.HasContentLinks);
        Assert.Null(viewModel.ContentShareHostText);
    }

    [Fact]
    public async Task Open_ListingOnAShareHost_NamesTheHost()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json =>
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "AdvancedFlightComputer")!;
            var releases = listing["authored"]!["releases"]!.AsObject();
            releases["ksamods"] = "AdvancedFlightComputer";
            releases["authority"] = "ksamods";
            return root.ToJsonString();
        });
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatContentSharedOn("ksamods"), viewModel.ContentShareHostText);
    }

    [Fact]
    public async Task Open_ListingOnARegisteredShareHost_LinksTheHostPage()
    {
        var hosts = ViewModelHarness.ShareHosts("""{ "ksamods": { "kind": "share", "name": "ksamods.gg", "api": "https://ksamods.gg/api/share/v1/", "types": ["vehicle", "mod"] } }""");
        using var harness = await ViewModelHarness.CreateAsync(
            editSnapshot: json =>
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
                var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "AdvancedFlightComputer")!;
                listing["authored"]!["releases"] = new System.Text.Json.Nodes.JsonObject { ["ksamods"] = "AdvancedFlightComputer" };
                return hosts(root.ToJsonString());
            });
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        Assert.Contains(new ContentLink(harness.Localization.FormatContentSharedOn("ksamods.gg"), "https://ksamods.gg/", "share-host"), viewModel.ContentLinks);
        Assert.Null(viewModel.ContentShareHostText);
    }

    [Fact]
    public async Task Open_ShowsCuratedTagsFirstInTheVocabularyWords()
    {
        var tags = ViewModelHarness.CuratedTags(("parts", "Parts"), ("physics", "Physics"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var armory = viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory");

        await armory.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasContentTags);
        Assert.Equal(["Parts", "Physics", "Weapons"], armory.AllTags);
    }

    [Fact]
    public async Task Versions_ListEveryReleaseNewestFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVersionsTab);
        Assert.False(viewModel.IsLoadingVersions);
        Assert.Equal(4, viewModel.ContentVersions.Count);
        Assert.Equal(
            viewModel.ContentVersions.OrderByDescending(version => version.ReleaseDate).Select(version => version.Version),
            viewModel.ContentVersions.Select(version => version.Version));
        Assert.All(viewModel.ContentVersions, version =>
        {
            Assert.True(version.CanInstall);
            Assert.False(string.IsNullOrWhiteSpace(version.ChannelText));
            Assert.False(string.IsNullOrWhiteSpace(version.PublishedText));
            Assert.StartsWith(version.GameVersionText.StartsWith(">= ", StringComparison.Ordinal) ? ">= " : "2026.", version.GameVersionText);
        });

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        Assert.Equal(4, viewModel.ContentVersions.Count);

        viewModel.ShowContentDescriptionCommand.Execute(null);
        Assert.True(viewModel.IsDescriptionTab);
    }

    [Fact]
    public async Task Versions_ReleaseWhoseDownloadIsGone_ShowsTheDate_AndInstallsOnlyWithAMirror()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot =>
            ViewModelHarness.MarkGone("AdvancedFlightComputer", "0.7.5")(ViewModelHarness.MarkGone("AdvancedFlightComputer", "0.7.4", keepMirrors: true)(snapshot)));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.Equal("0.7.3", viewModel.LatestVersion!.Version);
        var rows = viewModel.ContentVersions.ToDictionary(row => row.Version);
        Assert.Equal(["0.7.5", "0.7.4", "0.7.3", "0.7.2"], rows.Keys);
        var gone = harness.Localization.FormatContentVersionGone(MainViewModel.DateText(ViewModelHarness.GoneSince));
        Assert.Equal(gone, rows["0.7.5"].GoneText);
        Assert.False(rows["0.7.5"].CanInstall);
        Assert.Equal(gone, rows["0.7.4"].GoneText);
        Assert.True(rows["0.7.4"].CanInstall);
        Assert.Null(rows["0.7.3"].GoneText);
        Assert.True(rows["0.7.3"].CanInstall);
    }

    [Fact]
    public async Task Versions_WithoutAGame_AreUnknown()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.ContentVersions);
        Assert.All(viewModel.ContentVersions, version =>
        {
            Assert.Equal(GameCompatibility.Unknown, version.Compatibility);
            Assert.Equal(harness.Localization.CompatibilityUnknown, version.CompatibilityText);
        });
    }

    [Fact]
    public async Task Versions_EachReleaseFollowsTheInstalledGame()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var older));
        Assert.True(GameVersion.TryParse("2026.9.4.5400", out var newer));
        await viewModel.RefreshCompatibilityAsync(older);
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.Equal(
            new[] { ("0.7.5", GameCompatibility.Incompatible), ("0.7.4", GameCompatibility.Incompatible), ("0.7.3", GameCompatibility.Compatible), ("0.7.2", GameCompatibility.Incompatible) },
            viewModel.ContentVersions.Select(version => (version.Version, version.Compatibility)));

        await viewModel.RefreshCompatibilityAsync(newer);

        Assert.Equal(
            new[] { ("0.7.5", GameCompatibility.Compatible), ("0.7.4", GameCompatibility.Compatible), ("0.7.3", GameCompatibility.Incompatible), ("0.7.2", GameCompatibility.Incompatible) },
            viewModel.ContentVersions.Select(version => (version.Version, version.Compatibility)));
    }

    [Fact]
    public async Task Versions_MarkTheReleaseInTheActiveInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.Equal(["0.7.5"], viewModel.ContentVersions.Where(version => version.IsInstalled).Select(version => version.Version));

        await viewModel.DeactivateInstanceAsync();

        Assert.DoesNotContain(viewModel.ContentVersions, version => version.IsInstalled);
    }

    [Fact]
    public async Task Versions_OfAModInstalledInAnotherVersion_NameTheChange()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, version: "0.7.3");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        var rows = viewModel.ContentVersions.ToDictionary(row => row.Version);
        var newer = rows["0.7.5"];
        Assert.True(newer.CanInstall);
        Assert.Equal("0.7.3", newer.ReplacedVersion);
        Assert.Equal(harness.Localization.FormatContentReplaceVersionIn("0.7.3", "Main"), newer.InstallToolTip);
        Assert.Equal(harness.Localization.FormatContentReplaceVersion("0.7.3"), newer.ConfirmInstallText);
        newer.InstallWarning = "The release is untested.";
        Assert.Equal(harness.Localization.FormatContentReplaceVersionAnyway("0.7.3"), newer.ConfirmInstallText);

        var installed = rows["0.7.3"];
        Assert.True(installed.IsInstalled);
        Assert.False(installed.CanInstall);
        Assert.Null(installed.ReplacedVersion);
        Assert.Equal(viewModel.AddToText, installed.InstallToolTip);
        Assert.Equal(harness.Localization.ContentAdd, installed.ConfirmInstallText);

        await viewModel.DeactivateInstanceAsync();

        Assert.Null(newer.ReplacedVersion);
        Assert.Equal(viewModel.AddToText, newer.InstallToolTip);
        Assert.True(installed.CanInstall);
    }

    [Fact]
    public async Task ChangeVersion_OpensTheModPageOnTheVersionsTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, version: "0.7.3");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.True(afc.CanChangeVersion);

        await afc.ChangeVersionCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Same(afc, viewModel.SelectedContent);
        Assert.True(viewModel.IsVersionsTab);
        Assert.NotEmpty(viewModel.ContentVersions);
    }

    [Fact]
    public async Task Loader_OffersNoInstallButtons()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverLoadersCommand.Execute(null);
        var loader = viewModel.DiscoverItems.Single();
        await loader.OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderContent);
        Assert.All(viewModel.ContentVersions, version => Assert.False(version.CanInstall));
        Assert.False(loader.CanChangeVersion);
    }

    [Fact]
    public async Task LeavingThePage_ForgetsTheLastOutcome()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        await afc.OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        afc.InstallError = "old";
        afc.IsConfirmingRemove = true;
        viewModel.ContentVersions[0].InstallError = "old row";

        viewModel.SetMainWindowHome();

        Assert.Null(afc.InstallError);
        Assert.False(afc.IsConfirmingRemove);
        Assert.Null(viewModel.ContentVersions[0].InstallError);

        afc.InstallError = "old";
        await afc.OpenCommand.ExecuteAsync(null);
        Assert.Null(afc.InstallError);
    }

    [Fact]
    public async Task InstallVersion_DownloadFails_ShowsTheErrorOnTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        var oldest = viewModel.ContentVersions.Last();

        await oldest.InstallCommand.ExecuteAsync(null);
        Assert.NotNull(oldest.InstallWarning);
        await oldest.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.NotNull(oldest.InstallError);
        Assert.False(oldest.IsInstalling);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Versions_TextChangelogRendersOnTheRowAndAUrlShowsALink()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        harness.SpaceDock.Releases.AddRange(
        [
            Release("1.2.0", "## Fixes\n\n- The HUD keeps its place."),
            Release("1.1.0", "https://example.com/aircraft-hud/1.1.0"),
            Release("1.0.0", null),
        ]);
        var viewModel = harness.ViewModel;
        var listing = await harness.SpaceDock.GetListingAsync(ViewModelHarness.FakeSpaceDock.OwnId);
        await viewModel.OpenContentCommand.ExecuteAsync(new DiscoverItem(viewModel, listing!));

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        var rows = viewModel.ContentVersions.ToDictionary(row => row.Version);
        var text = rows["1.2.0"];
        Assert.Equal("## Fixes\n\n- The HUD keeps its place.", text.Changelog?.Text);
        Assert.Null(text.Changelog?.Link);
        Assert.False(text.IsChangelogExpanded);
        text.ToggleChangelogCommand.Execute(null);
        Assert.True(text.IsChangelogExpanded);

        var link = rows["1.1.0"].Changelog;
        Assert.NotNull(link);
        Assert.Null(link.Text);
        Assert.Equal(new ContentLink(harness.Localization.ContentChangelog, "https://example.com/aircraft-hud/1.1.0"), link.Link);

        Assert.Null(rows["1.0.0"].Changelog);
    }

    [Theory]
    [InlineData("https://example.com/changelog", true)]
    [InlineData("  https://example.com/changelog\n", true)]
    [InlineData("http://example.com/changelog", false)]
    [InlineData("/releases/1.0.0", false)]
    [InlineData("https://example.com/changelog fixes the HUD", false)]
    public void Changelog_OnlyAnAbsoluteHttpsUriIsALink(string value, bool isLink)
    {
        var changelog = ReleaseChangelog.From(Release("1.0.0", value), "1.0.0", "Changelog");

        Assert.NotNull(changelog);
        Assert.Equal(isLink, changelog.Link is not null);
        Assert.Equal(isLink, changelog.Text is null);
    }

    [Theory]
    [InlineData("## Changes\n\n- The HUD keeps its place.", true)]
    [InlineData(" \n", false)]
    [InlineData(null, false)]
    public void Changelog_TextIsPreferredOverTheLink(string? text, bool showsText)
    {
        var changelog = ReleaseChangelog.From(Release("1.0.0", "https://example.com/changelog", text), "1.0.0", "Changelog");

        Assert.NotNull(changelog);
        Assert.Equal(showsText ? text : null, changelog.Text);
        Assert.Equal(showsText, changelog.Link is null);
    }

    private static ModVersionMetadata Release(string version, string? changelog, string? changelogText = null) => new(
        specVersion: 1,
        modId: ViewModelHarness.FakeSpaceDock.OwnId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://archives.test/{version}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: [],
        changelog: changelog,
        changelogText: changelogText);

    [Fact]
    public async Task OpenContent_HomepageAndUnknownLinks_GetReadableLabels()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json =>
        {
            var listing = json.IndexOf("\"id\": \"AdvancedFlightComputer\",", StringComparison.Ordinal);
            var links = json.IndexOf("\"links\": {", listing, StringComparison.Ordinal) + "\"links\": {".Length;
            return json.Insert(links, " \"homepage\": \"https://example.com/afc\", \"wiki\": \"https://example.com/afc/wiki\",");
        });
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        var labels = viewModel.ContentLinks.Select(link => link.Label).ToList();
        Assert.Contains(harness.Localization.LinkHomepage, labels);
        Assert.Contains("Wiki", labels);
    }

    [Fact]
    public async Task Versions_ShowTheDownloadsOfEachRelease_SplitByHostInTheToolTip()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithReleaseDownloads);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        var rows = viewModel.ContentVersions.ToDictionary(row => row.Version);
        Assert.Equal("58", rows["0.7.5"].DownloadsText);
        Assert.Equal("GitHub 49, SpaceDock 9", rows["0.7.5"].DownloadsToolTip);
        Assert.Equal(MainViewModel.CompactCount(1503), rows["0.7.4"].DownloadsText);
        Assert.Equal($"Codeberg 3, GitHub {1500.ToString("N0", CultureInfo.CurrentCulture)}", rows["0.7.4"].DownloadsToolTip);
        Assert.Equal("58", viewModel.LatestVersion!.DownloadsText);
    }

    [Fact]
    public async Task Versions_ReleaseWithoutACount_ShowsNoDownloadsAndNotZero()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: WithReleaseDownloads);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        var afcUnknown = viewModel.ContentVersions.Where(row => row.Version is "0.7.3" or "0.7.2").ToList();

        await viewModel.DiscoverItems.Single(item => item.ModId == "MeasureTools").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.Equal(2, afcUnknown.Count);
        Assert.NotEmpty(viewModel.ContentVersions);
        Assert.All(afcUnknown.Concat(viewModel.ContentVersions), row =>
        {
            Assert.Null(row.DownloadsText);
            Assert.Null(row.DownloadsToolTip);
        });
    }

    /// <summary>Gives two releases of AdvancedFlightComputer a download count; 0.7.3 and 0.7.2 and every other listing keep none.</summary>
    internal static string WithReleaseDownloads(string json)
    {
        const string marker = "\"id\": \"AdvancedFlightComputer\",";
        var at = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return json.Insert(at, """
             "downloads": { "total": 1561, "hosts": { "codeberg": 3, "github": 1549, "spacedock": 9 }, "releases": [
               { "version": "0.7.5", "total": 58, "hosts": { "github": 49, "spacedock": 9 } },
               { "version": "0.7.4", "total": 1503, "hosts": { "codeberg": 3, "github": 1500 } } ] },
            """);
    }
}
