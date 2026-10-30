using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ContentDependenciesTests
{
    private static ModVersionMetadata Release(IReadOnlyList<ModDependency> dependencies) => new(
        specVersion: 1,
        modId: "Sample",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
        gameMin: "2026.9.7.5402",
        gameMinRevision: 5402,
        download: new DownloadInfo("https://example.invalid/sample.zip", new string('A', 64), null, "application/zip"),
        installSizeBytes: null,
        dependencies: dependencies);

    private static async Task<ViewModelHarness> CreateAsync(Func<string, string>? editSnapshot = null)
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: editSnapshot);
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return harness;
    }

    private static Task OpenAsync(MainViewModel viewModel, string modId, ContentType type = ContentType.Mod)
    {
        viewModel.DiscoverType = type;
        return viewModel.DiscoverItems.Single(item => item.ModId == modId).OpenCommand.ExecuteAsync(null);
    }

    private static Func<string, string> EditListings(Action<Func<string, JsonNode>> edit) => json =>
    {
        var root = JsonNode.Parse(json)!;
        edit(modId => root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!);
        return root.ToJsonString();
    };

    private static Func<string, string> EditReleases(string modId, Action<JsonArray> edit)
        => EditListings(listing => edit(listing(modId)["releases"]!.AsArray()));

    /// <summary>MeasureTools and <paramref name="copies"/> copies of it, each with a newest release that needs KSArmory and AdvancedFlightComputer.</summary>
    private static Task<ViewModelHarness> CreateWithDependentsAsync(int copies)
    {
        var needs = EditReleases("MeasureTools", releases => releases[0]!["dependencies"] = JsonNode.Parse("""
            [
                { "id": "KSArmory", "kind": "required", "source": "authored" },
                { "id": "AdvancedFlightComputer", "kind": "required", "source": "authored" }
            ]
            """));
        return CreateAsync(json => ViewModelHarness.WithCopies(needs(json), "MeasureTools", copies));
    }

    [Fact]
    public async Task Groups_FollowTheKindOrderAndListAnyOfMembersAsAlternatives()
    {
        using var harness = await CreateAsync();
        var release = Release(
        [
            new ModDependency("Unknown1", ModDependencyKind.Unknown),
            new ModDependency("Optional1", ModDependencyKind.Optional),
            new ModDependency("Suggested1", ModDependencyKind.Suggests),
            ModDependency.OfAlternatives(ModDependencyKind.Recommends, [new ModDependencyAlternative("RecommendedA"), new ModDependencyAlternative("RecommendedB")]),
            new ModDependency("Recommended1", ModDependencyKind.Recommends),
            new ModDependency("Conflict1", ModDependencyKind.Conflict),
            ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("AlternativeA", ModVersion.Parse("2.0.0")), new ModDependencyAlternative("AlternativeB")]),
            ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("AlternativeC")]),
            new ModDependency("Required1", ModDependencyKind.Required),
            new ModDependency("Required2", ModDependencyKind.Required),
        ]);

        var groups = harness.ViewModel.BuildDependencyGroups(release);

        Assert.Equal(
            [DependencyGroupKind.Required, DependencyGroupKind.AnyOf, DependencyGroupKind.Recommended, DependencyGroupKind.Suggested, DependencyGroupKind.Optional, DependencyGroupKind.Other],
            groups.Select(group => group.Kind));
        Assert.Equal(
            [harness.Localization.ContentDependencyRequired, harness.Localization.ContentDependencyAnyOf, harness.Localization.ContentDependencyRecommended, harness.Localization.ContentDependencySuggested, harness.Localization.ContentDependencyOptional, harness.Localization.ContentDependencyOther],
            groups.Select(group => group.Heading));
        Assert.Equal(harness.Localization.ContentDependencyRecommendedHint, groups[2].Hint);
        Assert.Equal(["Required1", "Required2"], groups[0].Entries.Select(entry => Assert.Single(entry.Members).ModId));

        var anyOf = groups[1].Entries;
        Assert.Equal(2, anyOf.Count);
        Assert.All(anyOf, entry =>
        {
            Assert.True(entry.IsAnyOf);
            Assert.False(entry.ShowsOneOf);
        });
        Assert.Equal(["AlternativeA", "AlternativeB"], anyOf[0].Members.Select(member => member.ModId));
        Assert.Equal("2.0.0 or newer", anyOf[0].Members[0].BoundsText);
        Assert.Null(anyOf[0].Members[1].BoundsText);
        Assert.Equal(["AlternativeC"], anyOf[1].Members.Select(member => member.ModId));

        var recommended = groups[2].Entries;
        Assert.Equal([true, false], recommended.Select(entry => entry.IsAnyOf));
        Assert.Equal([true, false], recommended.Select(entry => entry.ShowsOneOf));
        Assert.Equal(["RecommendedA", "RecommendedB"], recommended[0].Members.Select(member => member.ModId));
    }

    [Fact]
    public async Task BoundsText_NamesTheFourCases()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.Null(viewModel.BoundsText(null, null));
        Assert.Equal("1.2.0 or newer", viewModel.BoundsText(ModVersion.Parse("1.2.0"), null));
        Assert.Equal("up to 2.0.0", viewModel.BoundsText(null, ModVersion.Parse("2.0.0")));
        Assert.Equal("1.2.0 to 2.0.0", viewModel.BoundsText(ModVersion.Parse("1.2.0"), ModVersion.Parse("2.0.0")));
    }

    [Fact]
    public async Task Entries_UseTheIndexNameAndIconWhenListed_AndTheIdWhenNot()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var release = Release([new ModDependency("AdvancedFlightComputer", ModDependencyKind.Required), new ModDependency("NotListedMod", ModDependencyKind.Required)]);

        var members = Assert.Single(viewModel.BuildDependencyGroups(release)).Entries.Select(entry => Assert.Single(entry.Members)).ToList();

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.True(members[0].IsListed);
        Assert.Equal("Advanced Flight Computer", members[0].Name);
        Assert.Same(afc.Icon, members[0].Icon);
        Assert.False(members[1].IsListed);
        Assert.Equal("NotListedMod", members[1].Name);
        Assert.Null(members[1].Icon);

        await members[1].OpenCommand.ExecuteAsync(null);
        Assert.False(viewModel.CurrentWindowContent);

        await members[0].OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.CurrentWindowContent);
        Assert.Same(afc, viewModel.SelectedContent);
    }

    [Fact]
    public async Task Entries_FollowTheStateInTheActiveInstance()
    {
        using var harness = await CreateAsync(EditReleases("AdvancedFlightComputer", releases =>
            releases[0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "required", "source": "authored" }, { "id": "KSArmory", "kind": "optional", "source": "authored" }]""")));
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "AdvancedFlightComputer");

        Assert.All(viewModel.ContentDependencyGroups.SelectMany(group => group.Entries).SelectMany(entry => entry.Members), member =>
        {
            Assert.Null(member.StateText);
            Assert.False(member.IsInstalled);
        });

        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true);
        await viewModel.LoadAsync();

        var installed = Assert.Single(viewModel.ContentDependencyGroups[0].Entries[0].Members);
        Assert.True(installed.IsInstalled);
        Assert.True(installed.IsInstalledWithoutConflict);
        Assert.Equal("Installed 1.1.10", installed.StateText);
        Assert.Equal(viewModel.InstalledInText, installed.StateTip);
        var missing = Assert.Single(viewModel.ContentDependencyGroups[1].Entries[0].Members);
        Assert.False(missing.IsInstalled);
        Assert.Equal(harness.Localization.ContentDependencyNotInstalled, missing.StateText);
        Assert.Null(missing.StateTip);
    }

    [Fact]
    public async Task Conflict_ThatIsInstalledWithinItsBounds_IsMarked()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true);
        await viewModel.LoadAsync();

        var within = Assert.Single(viewModel.BuildConflictMembers("Sample", Release([new ModDependency("MeasureTools", ModDependencyKind.Conflict)])));
        var outside = Assert.Single(viewModel.BuildConflictMembers("Sample", Release([new ModDependency("MeasureTools", ModDependencyKind.Conflict, maxVersion: ModVersion.Parse("1.0.0"))])));
        var absent = Assert.Single(viewModel.BuildConflictMembers("Sample", Release([new ModDependency("KSArmory", ModDependencyKind.Conflict)])));
        var twoRanges = Assert.Single(viewModel.BuildConflictMembers("Sample", Release(
        [
            new ModDependency("MeasureTools", ModDependencyKind.Conflict, maxVersion: ModVersion.Parse("1.0.0")),
            new ModDependency("MeasureTools", ModDependencyKind.Conflict, minVersion: ModVersion.Parse("1.1.0")),
        ])));

        Assert.True(within.IsConflicting);
        Assert.False(within.IsInstalledWithoutConflict);
        Assert.False(outside.IsConflicting);
        Assert.True(outside.IsInstalled);
        Assert.False(absent.IsConflicting);
        Assert.True(twoRanges.IsConflicting);
    }

    [Fact]
    public async Task Conflict_WithSeveralEntriesForOneMod_ShowsEveryRangeOnItsRow()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var upToOne = new ModDependency("MeasureTools", ModDependencyKind.Conflict, maxVersion: ModVersion.Parse("1.0.0"));

        var twoRanges = Assert.Single(viewModel.BuildConflictMembers("Sample", Release([upToOne, new ModDependency("MeasureTools", ModDependencyKind.Conflict, minVersion: ModVersion.Parse("1.1.0"))])));
        var sameRange = Assert.Single(viewModel.BuildConflictMembers("Sample", Release([upToOne, upToOne])));
        var everyVersion = Assert.Single(viewModel.BuildConflictMembers("Sample", Release([upToOne, new ModDependency("MeasureTools", ModDependencyKind.Conflict)])));

        Assert.Equal("up to 1.0.0, 1.1.0 or newer", twoRanges.BoundsText);
        Assert.Equal("up to 1.0.0", sameRange.BoundsText);
        Assert.Null(everyVersion.BoundsText);
    }

    [Fact]
    public async Task Tab_ShowsTheLoaderAndTheDependenciesOfTheReleaseInTheHeader()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "AdvancedFlightComputer");

        viewModel.ShowContentDependenciesCommand.Execute(null);

        Assert.True(viewModel.IsDependenciesTab);
        Assert.False(viewModel.IsDescriptionTab);
        Assert.False(viewModel.IsVersionsTab);
        Assert.True(viewModel.HasContentDependenciesTab);
        Assert.Equal("Dependencies of 0.7.5", viewModel.ContentDependenciesHeading);
        Assert.Null(viewModel.ContentDependenciesEmptyText);
        Assert.Equal("Needs StarMap 0.4.5 or newer", viewModel.ContentLoaderDependency!.Title);
        Assert.Null(viewModel.ContentLoaderDependency.BoundsText);
        Assert.True(viewModel.ContentLoaderDependency.IsListed);
        Assert.Null(viewModel.ContentLoaderDependency.StateText);
        var group = Assert.Single(viewModel.ContentDependencyGroups);
        Assert.Equal(DependencyGroupKind.Optional, group.Kind);
        var member = Assert.Single(Assert.Single(group.Entries).Members);
        Assert.Equal("KittenExtensions", member.Name);
        Assert.False(member.IsListed);

        harness.Localization.TrySetCulture("de");
        Assert.Equal(harness.Localization.FormatContentDependenciesOf("0.7.5"), viewModel.ContentDependenciesHeading);
        Assert.NotEqual("Dependencies of 0.7.5", viewModel.ContentDependenciesHeading);
        Assert.Equal(harness.Localization.ContentDependencyOptional, viewModel.ContentDependencyGroups[0].Heading);

        await OpenAsync(viewModel, "KSArmory");
        Assert.True(viewModel.IsDescriptionTab);
    }

    [Fact]
    public async Task Tab_WithoutDependencies_SaysSo()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "MeasureTools");

        Assert.Empty(viewModel.ContentDependencyGroups);
        Assert.Equal(harness.Localization.ContentNoDependencies, viewModel.ContentDependenciesEmptyText);
        Assert.Equal("Needs StarMap 0.4.5 or newer", viewModel.ContentLoaderDependency!.Title);
    }

    [Fact]
    public async Task Tab_OnALoaderPage_ShowsOnlyWhenTheReleaseHasDependencies()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "AdvancedFlightComputer");
        viewModel.ShowContentDependenciesCommand.Execute(null);

        await OpenAsync(viewModel, "StarMap", ContentType.ModLoader);

        Assert.True(viewModel.IsLoaderContent);
        Assert.False(viewModel.HasContentDependenciesTab);
        Assert.Null(viewModel.ContentLoaderDependency);
        Assert.True(viewModel.IsDescriptionTab);
    }

    [Fact]
    public async Task Tab_OnALoaderPage_FallsBackToTheDescriptionWhenTheNewReleaseHasNoDependencies()
    {
        var addDev = SnapshotRelease.Add(new SnapshotRelease("StarMap", "0.5.0-dev.1", "dev", "2026-09-10T10:00:00Z"));
        var setDependencies = EditReleases("StarMap", releases =>
            releases[0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "suggests", "source": "authored" }]"""));
        using var harness = await CreateAsync(json => setDependencies(addDev(json)));
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "StarMap", ContentType.ModLoader);
        viewModel.ShowContentDependenciesCommand.Execute(null);
        Assert.True(viewModel.IsDependenciesTab);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.Equal("0.5.0-dev.1", viewModel.LatestVersion!.Version);
        Assert.False(viewModel.HasContentDependenciesTab);
        Assert.True(viewModel.IsDescriptionTab);
    }

    [Fact]
    public async Task Tab_OnALoaderPage_ShowsTheDependenciesOfItsRelease()
    {
        using var harness = await CreateAsync(EditReleases("StarMap", releases =>
            releases[0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "suggests", "source": "authored" }]""")));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "StarMap", ContentType.ModLoader);

        Assert.True(viewModel.HasContentDependenciesTab);
        Assert.Equal(DependencyGroupKind.Suggested, Assert.Single(viewModel.ContentDependencyGroups).Kind);
    }

    [Fact]
    public async Task Tab_OnALoaderPage_ShowsWhenARequiredEntryNamesTheLoader()
    {
        using var harness = await CreateAsync(EditReleases("AdvancedFlightComputer", releases =>
            releases[0]!["dependencies"] = JsonNode.Parse("""[{ "id": "StarMap", "kind": "required", "source": "authored" }]""")));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "StarMap", ContentType.ModLoader);

        Assert.True(viewModel.HasContentDependenciesTab);
        Assert.Empty(viewModel.ContentDependencyGroups);
        viewModel.ToggleContentRequiredByCommand.Execute(null);
        Assert.Equal("AdvancedFlightComputer", Assert.Single(viewModel.ContentRequiredBy).ModId);
    }

    [Fact]
    public async Task RequiredBy_ListsOnlyARequiredEntryOfTheNewestRelease()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("AdvancedFlightComputer")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "required", "min": "0.8.0", "source": "authored" }]""");
            var measureTools = listing("MeasureTools")["releases"]!;
            measureTools[0]!["dependencies"] = JsonNode.Parse("""
                [
                    { "id": "KSArmory", "kind": "recommends", "source": "authored" },
                    { "id": "KSArmory", "kind": "suggests", "source": "authored" },
                    { "id": "KSArmory", "kind": "optional", "source": "authored" },
                    { "id": "KSArmory", "kind": "conflict", "source": "authored" },
                    { "kind": "required", "any_of": [{ "id": "KSArmory" }, { "id": "AdvancedFlightComputer" }], "source": "authored" }
                ]
                """);
            measureTools[1]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "required", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");
        viewModel.ToggleContentRequiredByCommand.Execute(null);

        Assert.True(viewModel.HasContentRequiredBy);
        var dependent = Assert.Single(viewModel.ContentRequiredBy);
        Assert.Equal("AdvancedFlightComputer", dependent.ModId);
        Assert.Equal("Advanced Flight Computer", dependent.Name);
        Assert.Null(dependent.BoundsText);
        Assert.Null(dependent.StateText);

        await dependent.OpenCommand.ExecuteAsync(null);

        Assert.Equal("AdvancedFlightComputer", viewModel.SelectedContent!.ModId);
        Assert.Empty(viewModel.ContentRequiredBy);
        Assert.False(viewModel.HasContentRequiredBy);
    }

    [Fact]
    public async Task RequiredBy_IsMostDownloadedFirst_AndShowsOnlyTheInstalledState()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            var needsArmory = """[{ "id": "KSArmory", "kind": "required", "source": "authored" }]""";
            listing("AdvancedFlightComputer")["releases"]![0]!["dependencies"] = JsonNode.Parse(needsArmory);
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse(needsArmory);
            listing("MeasureTools")["downloads"] = JsonNode.Parse("""{ "total": 1234, "hosts": { "github": 1234 } }""");
        }));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "KSArmory");
        viewModel.ToggleContentRequiredByCommand.Execute(null);

        Assert.Equal(["MeasureTools", "AdvancedFlightComputer"], viewModel.ContentRequiredBy.Select(member => member.ModId));
        Assert.All(viewModel.ContentRequiredBy, member => Assert.Null(member.BoundsText));
        Assert.Equal("Installed 1.1.10", viewModel.ContentRequiredBy[0].StateText);
        Assert.True(viewModel.ContentRequiredBy[0].IsInstalled);
        Assert.Null(viewModel.ContentRequiredBy[1].StateText);
        Assert.False(viewModel.ContentRequiredBy[1].IsInstalled);
    }

    [Fact]
    public async Task RequiredBy_ShowsOnAPageWithoutARelease()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("KSArmory")["releases"]![0]!["release_status"] = "dev";
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "required", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");
        viewModel.ToggleContentRequiredByCommand.Execute(null);

        Assert.Null(viewModel.LatestVersion);
        Assert.Equal("MeasureTools", Assert.Single(viewModel.ContentRequiredBy).ModId);
    }

    [Fact]
    public async Task RequiredBy_FollowsAChangeOfTheReleaseChannelOnAPageWithoutARelease()
    {
        var addTesting = SnapshotRelease.Add(new SnapshotRelease("MeasureTools", "1.2.0-rc.1", "testing", "2026-09-20T10:00:00Z"));
        var edit = EditListings(listing =>
        {
            foreach (var release in listing("KSArmory")["releases"]!.AsArray())
                release!["release_status"] = "dev";
            listing("MeasureTools")["releases"]!.AsArray().Single(node => (string?)node!["version"] == "1.2.0-rc.1")!["dependencies"] =
                JsonNode.Parse("""[{ "id": "KSArmory", "kind": "required", "source": "authored" }]""");
        });
        using var harness = await CreateAsync(json => edit(addTesting(json)));
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "KSArmory");
        viewModel.ToggleContentRequiredByCommand.Execute(null);
        Assert.Empty(viewModel.ContentRequiredBy);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Testing);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.Null(viewModel.LatestVersion);
        Assert.Equal("MeasureTools", Assert.Single(viewModel.ContentRequiredBy).ModId);
    }

    [Fact]
    public async Task RequiredBy_ByDefault_ShowsOnlyTheListingsThatTheInstanceHolds()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools1", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "KSArmory");

        Assert.True(viewModel.HasContentRequiredBy);
        Assert.Equal("MeasureTools1", Assert.Single(viewModel.ContentRequiredBy).ModId);
        Assert.False(viewModel.IsContentRequiredByNoneInstalled);
        Assert.Equal("Show all 3", viewModel.ContentRequiredByToggleText);
    }

    [Fact]
    public async Task RequiredBy_ShowAllShowsEveryListing_AndShowOnlyInstalledGoesBack()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools1", activate: true);
        await viewModel.LoadAsync();
        await OpenAsync(viewModel, "KSArmory");

        viewModel.ToggleContentRequiredByCommand.Execute(null);

        Assert.Equal(["MeasureTools", "MeasureTools1", "MeasureTools2"], viewModel.ContentRequiredBy.Select(member => member.ModId));
        Assert.Equal("Show only installed", viewModel.ContentRequiredByToggleText);

        viewModel.ToggleContentRequiredByCommand.Execute(null);

        Assert.Equal("MeasureTools1", Assert.Single(viewModel.ContentRequiredBy).ModId);
        Assert.Equal("Show all 3", viewModel.ContentRequiredByToggleText);
    }

    [Fact]
    public async Task RequiredBy_WithoutAnActiveInstance_ShowsTheLineAndTheLink()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");

        Assert.True(viewModel.HasContentRequiredBy);
        Assert.Empty(viewModel.ContentRequiredBy);
        Assert.True(viewModel.IsContentRequiredByNoneInstalled);
        Assert.Equal("Show all 3", viewModel.ContentRequiredByToggleText);
    }

    [Fact]
    public async Task RequiredBy_WhenTheInstanceHoldsNoListing_ShowsTheLineUntilShowAll()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "KSArmory");

        Assert.Empty(viewModel.ContentRequiredBy);
        Assert.True(viewModel.IsContentRequiredByNoneInstalled);

        viewModel.ToggleContentRequiredByCommand.Execute(null);

        Assert.Equal(3, viewModel.ContentRequiredBy.Count);
        Assert.False(viewModel.IsContentRequiredByNoneInstalled);
    }

    [Fact]
    public async Task RequiredBy_WithoutAListing_ShowsNoCard()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "MeasureTools");

        Assert.False(viewModel.HasContentRequiredBy);
        Assert.False(viewModel.IsContentRequiredByNoneInstalled);
    }

    [Fact]
    public async Task RequiredBy_AfterShowAll_StaysWhenTheLanguageChanges()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "KSArmory");
        viewModel.ToggleContentRequiredByCommand.Execute(null);

        harness.Localization.TrySetCulture("de");

        Assert.Equal(3, viewModel.ContentRequiredBy.Count);
        Assert.Equal(harness.Localization.ContentDependencyRequiredByShowInstalled, viewModel.ContentRequiredByToggleText);
        Assert.NotEqual("Show only installed", viewModel.ContentRequiredByToggleText);
    }

    [Fact]
    public async Task RequiredBy_OnAnotherPage_ShowsOnlyTheInstalledListingsAgain()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "KSArmory");
        viewModel.ToggleContentRequiredByCommand.Execute(null);

        await OpenAsync(viewModel, "AdvancedFlightComputer");

        Assert.Empty(viewModel.ContentRequiredBy);
        Assert.True(viewModel.IsContentRequiredByNoneInstalled);
        Assert.Equal("Show all 3", viewModel.ContentRequiredByToggleText);
    }

    [Fact]
    public async Task Conflicts_ListBothDirections_AndARowOpensItsMod()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("KSArmory")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "conflict", "max": "1.0.0", "source": "authored" }]""");
            listing("AdvancedFlightComputer")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "conflict", "max": "1.0.0", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");

        Assert.True(viewModel.HasContentConflicts);
        Assert.Equal(["MeasureTools", "AdvancedFlightComputer"], viewModel.ContentConflicts.Select(member => member.ModId));
        Assert.Equal("up to 1.0.0", viewModel.ContentConflicts[0].BoundsText);
        Assert.Null(viewModel.ContentConflicts[1].BoundsText);

        await viewModel.ContentConflicts[1].OpenCommand.ExecuteAsync(null);

        Assert.Equal("AdvancedFlightComputer", viewModel.SelectedContent!.ModId);
        Assert.Equal("KSArmory", Assert.Single(viewModel.ContentConflicts).ModId);
    }

    [Fact]
    public async Task Conflicts_OfListings_AreMostDownloadedFirst_AndAnOlderReleaseDoesNotCount()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            var namesArmory = """[{ "id": "KSArmory", "kind": "conflict", "source": "authored" }]""";
            listing("AdvancedFlightComputer")["releases"]![0]!["dependencies"] = JsonNode.Parse(namesArmory);
            listing("StarMap")["releases"]![0]!["dependencies"] = JsonNode.Parse(namesArmory);
            listing("StarMap")["downloads"] = JsonNode.Parse("""{ "total": 1234, "hosts": { "github": 1234 } }""");
            listing("MeasureTools")["releases"]![1]!["dependencies"] = JsonNode.Parse(namesArmory);
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");

        Assert.Equal(["StarMap", "AdvancedFlightComputer"], viewModel.ContentConflicts.Select(member => member.ModId));
    }

    [Fact]
    public async Task Conflicts_OfAModInBothDirections_ShowOneRow()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("KSArmory")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "conflict", "max": "1.0.0", "source": "authored" }]""");
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "ksarmory", "kind": "conflict", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");

        var member = Assert.Single(viewModel.ContentConflicts);
        Assert.Equal("MeasureTools", member.ModId);
        Assert.Null(member.BoundsText);
    }

    [Fact]
    public async Task Conflicts_OfAHeldModInBothDirections_ShowOneMarkedRowWithoutARange()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("KSArmory")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "conflict", "max": "1.0.0", "source": "authored" }]""");
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "conflict", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "KSArmory");

        var member = Assert.Single(viewModel.ContentConflicts);
        Assert.True(member.IsConflicting);
        Assert.Null(member.BoundsText);
    }

    [Fact]
    public async Task Conflicts_WhenTheListingIsOutsideTheBoundsOfThisRelease_KeepTheRangeOfTheNamedEntry()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("KSArmory")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "conflict", "max": "1.0.0", "source": "authored" }]""");
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "conflict", "max": "0.1.0", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");

        Assert.Equal("up to 1.0.0", Assert.Single(viewModel.ContentConflicts).BoundsText);
    }

    [Fact]
    public async Task Conflicts_AreNotADependencyGroup()
    {
        using var harness = await CreateAsync(EditReleases("KSArmory", releases =>
            releases[0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "conflict", "source": "authored" }]""")));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");

        Assert.Empty(viewModel.ContentDependencyGroups);
        Assert.Equal(harness.Localization.ContentNoDependencies, viewModel.ContentDependenciesEmptyText);
        Assert.Equal("MeasureTools", Assert.Single(viewModel.ContentConflicts).ModId);
    }

    [Fact]
    public async Task Conflicts_WithoutAConflictEntry_ShowNoCard()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("KSArmory")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "required", "source": "authored" }]""");
            listing("AdvancedFlightComputer")["releases"]![0]!["dependencies"] = JsonNode.Parse("""
                [
                    { "id": "KSArmory", "kind": "required", "source": "authored" },
                    { "id": "KSArmory", "kind": "recommends", "source": "authored" },
                    { "id": "KSArmory", "kind": "suggests", "source": "authored" },
                    { "kind": "required", "any_of": [{ "id": "KSArmory" }, { "id": "MeasureTools" }], "source": "authored" }
                ]
                """);
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "KSArmory");

        Assert.Empty(viewModel.ContentConflicts);
        Assert.False(viewModel.HasContentConflicts);
    }

    [Fact]
    public async Task Conflicts_OfAListing_ShowOnlyWithinTheBoundsOfThisRelease()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "AdvancedFlightComputer", "kind": "conflict", "max": "0.1.0", "source": "authored" }, { "id": "AdvancedFlightComputer", "kind": "conflict", "min": "0.7.0", "source": "authored" }]""");
            listing("KSArmory")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "AdvancedFlightComputer", "kind": "conflict", "max": "0.1.0", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true);
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "AdvancedFlightComputer");

        var member = Assert.Single(viewModel.ContentConflicts);
        Assert.Equal("MeasureTools", member.ModId);
        Assert.True(member.IsConflicting);
        Assert.Equal(harness.Localization.FormatContentDependencyInstalled("1.1.10"), member.StateText);
    }

    [Fact]
    public async Task Conflict_OfAListingThatTheInstanceDoesNotHold_IsNotMarked()
    {
        using var harness = await CreateAsync(EditReleases("MeasureTools", releases =>
            releases[0]!["dependencies"] = JsonNode.Parse("""[{ "id": "AdvancedFlightComputer", "kind": "conflict", "source": "authored" }]""")));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "AdvancedFlightComputer");

        var member = Assert.Single(viewModel.ContentConflicts);
        Assert.False(member.IsConflicting);
        Assert.False(member.IsInstalled);
        Assert.Equal(harness.Localization.ContentDependencyNotInstalled, member.StateText);
    }

    [Fact]
    public async Task Conflict_OnAPageWithoutARelease_ListsAHeldListingWithoutAMark()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("KSArmory")["releases"]![0]!["release_status"] = "dev";
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "conflict", "max": "0.1.0", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "KSArmory");

        Assert.Null(viewModel.LatestVersion);
        var member = Assert.Single(viewModel.ContentConflicts);
        Assert.Equal("MeasureTools", member.ModId);
        Assert.False(member.IsConflicting);
        Assert.True(member.IsInstalledWithoutConflict);
    }

    [Fact]
    public async Task Cards_FollowAModAddedWhileThePageIsOpen()
    {
        using var harness = await CreateAsync(EditListings(listing =>
        {
            listing("MeasureTools")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "required", "source": "authored" }]""");
            listing("AdvancedFlightComputer")["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{ "id": "KSArmory", "kind": "conflict", "source": "authored" }]""");
        }));
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "KSArmory");
        Assert.Empty(viewModel.ContentRequiredBy);
        Assert.False(Assert.Single(viewModel.ContentConflicts).IsConflicting);

        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true);
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();

        Assert.Equal("MeasureTools", Assert.Single(viewModel.ContentRequiredBy).ModId);
        Assert.False(viewModel.IsContentRequiredByNoneInstalled);
        Assert.True(Assert.Single(viewModel.ContentConflicts).IsConflicting);
    }

    [Fact]
    public async Task RequiredBy_AfterShowAll_StaysWhenAModIsAdded()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "KSArmory");
        viewModel.ToggleContentRequiredByCommand.Execute(null);

        await InstalledContent.AddAsync(harness, "MeasureTools1", activate: true);
        await viewModel.LoadAsync();

        Assert.Equal([false, true, false], viewModel.ContentRequiredBy.Select(member => member.IsInstalled));
        Assert.Equal("Show only installed", viewModel.ContentRequiredByToggleText);
    }

    [Fact]
    public async Task RequiredBy_ByDefault_FindsAHeldModWhoseIdDiffersInCase()
    {
        using var harness = await CreateWithDependentsAsync(copies: 2);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "measuretools1", activate: true);
        await viewModel.LoadAsync();

        await OpenAsync(viewModel, "KSArmory");

        Assert.Equal("MeasureTools1", Assert.Single(viewModel.ContentRequiredBy).ModId);
    }

    [Theory]
    [InlineData("StarMap", "MeasureTools")]
    [InlineData("MeasureTools", "StarMap")]
    public async Task Tab_OnALoaderPage_ShowsForAConflictInEitherDirection(string listing, string named)
    {
        using var harness = await CreateAsync(EditReleases(listing, releases =>
            releases[0]!["dependencies"] = JsonNode.Parse($$"""[{ "id": "{{named}}", "kind": "conflict", "source": "authored" }]""")));

        await OpenAsync(harness.ViewModel, "StarMap", ContentType.ModLoader);

        Assert.True(harness.ViewModel.HasContentDependenciesTab);
    }

    [Fact]
    public async Task Tab_WithoutARelease_SaysSo()
    {
        using var harness = await CreateAsync(EditReleases("AdvancedFlightComputer", releases =>
        {
            foreach (var release in releases)
                release!["release_status"] = "dev";
        }));
        var viewModel = harness.ViewModel;

        await OpenAsync(viewModel, "AdvancedFlightComputer");

        Assert.Null(viewModel.LatestVersion);
        Assert.Null(viewModel.ContentDependenciesHeading);
        Assert.Null(viewModel.ContentLoaderDependency);
        Assert.Empty(viewModel.ContentDependencyGroups);
        Assert.Equal(harness.Localization.ContentDependenciesNoRelease, viewModel.ContentDependenciesEmptyText);
    }

    [Fact]
    public async Task Tab_FollowsAChangeOfTheReleaseChannel()
    {
        var addDev = SnapshotRelease.Add(new SnapshotRelease("AdvancedFlightComputer", "0.8.0-dev.1", "dev", "2026-09-10T10:00:00Z"));
        var setDependencies = EditReleases("AdvancedFlightComputer", releases =>
            releases.Single(node => (string?)node!["version"] == "0.8.0-dev.1")!["dependencies"] =
                JsonNode.Parse("""[{ "id": "MeasureTools", "kind": "required", "min": "1.1.0", "source": "authored" }]"""));
        using var harness = await CreateAsync(json => setDependencies(addDev(json)));
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel, "AdvancedFlightComputer");
        viewModel.ShowContentDependenciesCommand.Execute(null);
        Assert.Equal(DependencyGroupKind.Optional, Assert.Single(viewModel.ContentDependencyGroups).Kind);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.True(viewModel.IsDependenciesTab);
        Assert.Equal("Dependencies of 0.8.0-dev.1", viewModel.ContentDependenciesHeading);
        var group = Assert.Single(viewModel.ContentDependencyGroups);
        Assert.Equal(DependencyGroupKind.Required, group.Kind);
        var member = Assert.Single(Assert.Single(group.Entries).Members);
        Assert.Equal("MeasureTools", member.ModId);
        Assert.Equal("1.1.0 or newer", member.BoundsText);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Stable);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.Equal("Dependencies of 0.7.5", viewModel.ContentDependenciesHeading);
        Assert.Equal(DependencyGroupKind.Optional, Assert.Single(viewModel.ContentDependencyGroups).Kind);
    }

    [Fact]
    public async Task Tab_FollowsAChangeToAChannelWithoutARelease()
    {
        using var harness = await CreateAsync(EditReleases("AdvancedFlightComputer", releases =>
        {
            foreach (var release in releases)
                release!["release_status"] = "dev";
        }));
        var viewModel = harness.ViewModel;
        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Dev);
        await viewModel.WhenReleaseChannelSavedAsync();
        await OpenAsync(viewModel, "AdvancedFlightComputer");
        viewModel.ShowContentDependenciesCommand.Execute(null);
        Assert.Equal("Dependencies of 0.7.5", viewModel.ContentDependenciesHeading);
        Assert.NotNull(viewModel.ContentLoaderDependency);
        Assert.NotEmpty(viewModel.ContentDependencyGroups);

        viewModel.SelectedReleaseChannel = viewModel.OptionFor(ReleaseChannel.Stable);
        await viewModel.WhenReleaseChannelSavedAsync();

        Assert.Null(viewModel.LatestVersion);
        Assert.Null(viewModel.ContentDependenciesHeading);
        Assert.Null(viewModel.ContentLoaderDependency);
        Assert.Empty(viewModel.ContentDependencyGroups);
        Assert.Equal(harness.Localization.ContentDependenciesNoRelease, viewModel.ContentDependenciesEmptyText);
    }
}
