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

    private static Func<string, string> EditReleases(string modId, Action<JsonArray> edit) => json =>
    {
        var root = JsonNode.Parse(json)!;
        edit(root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!["releases"]!.AsArray());
        return root.ToJsonString();
    };

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
            [DependencyGroupKind.Required, DependencyGroupKind.AnyOf, DependencyGroupKind.Conflicts, DependencyGroupKind.Recommended, DependencyGroupKind.Suggested, DependencyGroupKind.Optional, DependencyGroupKind.Other],
            groups.Select(group => group.Kind));
        Assert.Equal(
            [harness.Localization.ContentDependencyRequired, harness.Localization.ContentDependencyAnyOf, harness.Localization.ContentDependencyConflicts, harness.Localization.ContentDependencyRecommended, harness.Localization.ContentDependencySuggested, harness.Localization.ContentDependencyOptional, harness.Localization.ContentDependencyOther],
            groups.Select(group => group.Heading));
        Assert.Equal(harness.Localization.ContentDependencyConflictsHint, groups[2].Hint);
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

        var recommended = groups[3].Entries;
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
        var release = Release(
        [
            new ModDependency("MeasureTools", ModDependencyKind.Conflict),
            new ModDependency("MeasureTools", ModDependencyKind.Conflict, maxVersion: ModVersion.Parse("1.0.0")),
            new ModDependency("KSArmory", ModDependencyKind.Conflict),
        ]);

        var members = Assert.Single(viewModel.BuildDependencyGroups(release)).Entries.Select(entry => Assert.Single(entry.Members)).ToList();

        Assert.True(members[0].IsConflicting);
        Assert.False(members[0].IsInstalledWithoutConflict);
        Assert.False(members[1].IsConflicting);
        Assert.True(members[1].IsInstalled);
        Assert.False(members[2].IsConflicting);
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
