using System.Text.Json.Nodes;
using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Storage.Listings;
using Borea.Storage.Mods;

namespace Borea.Storage.Tests.Listings;

/// <summary>
/// Every case of the copy of schemas/pack-vectors.json of content-index, as schemas/README.md of that repository describes them.
/// The csproj names the commit the copy came from.
/// </summary>
public sealed class PackMemberRulesTests
{
    private static readonly JsonArray Vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Listings", "Fixtures", "pack-vectors.json")))!["vectors"]!.AsArray();

    private readonly TomlListingFormat _format = new();

    public static TheoryData<string> Names
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var vector in Vectors)
                names.Add((string)vector!["name"]!);
            return names;
        }
    }

    [Fact]
    public void Vectors_AreThere()
    {
        Assert.True(Vectors.Count >= 32, $"the copy has {Vectors.Count} cases");
        Assert.Contains(Vectors, vector => ((string)vector!["name"]!).Contains("content-index#117", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Check_GivesTheErrorsAndNotesOfContentIndex(string name)
    {
        var vector = Vector(name);
        var snapshot = Snapshot(vector["snapshot"]!.AsObject());

        var issues = PackMemberRules.Check(_format.Read((string)vector["pack"]!), snapshot).ToList();

        Assert.Equal(Expected(vector, "errors"), Lines(issues, ListingIssueSeverity.Error));
        Assert.Equal(Expected(vector, "notes"), Lines(issues, ListingIssueSeverity.Note));
        Assert.Equal((bool)vector["accepted"]!, !issues.Any(issue => issue.Severity == ListingIssueSeverity.Error));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Missing_AddsThePinsOfTheListingPage_AndThePackThenPasses(string name)
    {
        var vector = Vector(name);
        var snapshot = Snapshot(vector["snapshot"]!.AsObject());
        var draft = ListingDraft.FromDocument(_format.Read((string)vector["pack"]!));

        var fix = ListingPackDependencies.Missing(snapshot, draft.Mods);

        var adds = vector["adds"]?.AsArray().Select(pin => ((string)pin!["id"]!, (string)pin["version"]!)).ToList() ?? [];
        Assert.Equal(adds, fix.Pins.Select(pin => (pin.Id, pin.Version)));
        if (adds.Count > 0)
        {
            var fixedPack = (draft with { Mods = [.. draft.Mods, .. fix.Pins] }).ToDocument();
            Assert.DoesNotContain(PackMemberRules.Check(fixedPack, snapshot), issue => issue.Severity == ListingIssueSeverity.Error);
        }
    }

    [Fact]
    public void Missing_LeavesAnAnyOfAndAModThatIsNotListedToTheAuthor()
    {
        var snapshot = Snapshot(JsonNode.Parse("""
            { "listings": [
              { "id": "Alpha", "type": "mod", "releases": [ { "version": "1.0.0", "release_status": "stable", "dependencies": [
                { "kind": "required", "any_of": [ { "id": "Beta" }, { "id": "Gamma" } ] },
                { "kind": "required", "id": "Unlisted" },
                { "kind": "required", "any_of": [ { "id": "Gone" }, { "id": "Other" } ] },
                { "kind": "required", "id": "Beta", "min": "9.0.0" } ] } ] },
              { "id": "Beta", "type": "mod", "releases": [ { "version": "1.0.0", "release_status": "stable" } ] },
              { "id": "Gamma", "type": "mod", "releases": [ { "version": "1.0.0", "release_status": "stable" } ] } ] }
            """)!.AsObject());

        var fix = ListingPackDependencies.Missing(snapshot, [new ListingPackMember("Alpha", "1.0.0")]);

        Assert.Empty(fix.Pins);
        Assert.Equal(
            [
                (ListingPackNeedReason.Choose, "'Alpha' 1.0.0 requires one of 'Beta', 'Gamma'"),
                (ListingPackNeedReason.NotListed, "'Alpha' 1.0.0 requires 'Unlisted'"),
                (ListingPackNeedReason.NotListed, "'Alpha' 1.0.0 requires one of 'Gone', 'Other'"),
                (ListingPackNeedReason.NoRelease, "'Alpha' 1.0.0 requires 'Beta' 9.0.0 or newer"),
            ],
            fix.Needs.Select(need => (need.Reason, need.Text)));
    }

    [Fact]
    public void Fits_RefusesAReleaseThatTheOtherPinsDoNotAccept()
    {
        var snapshot = Snapshot(JsonNode.Parse("""
            { "listings": [
              { "id": "Alpha", "type": "mod", "releases": [
                { "version": "2.0.0", "release_status": "stable", "dependencies": [ { "kind": "required", "id": "Beta", "max": "1.0.0" } ] },
                { "version": "1.0.0", "release_status": "stable" } ] },
              { "id": "Beta", "type": "mod", "releases": [
                { "version": "2.0.0", "release_status": "stable" },
                { "version": "1.0.0", "release_status": "stable", "dependencies": [ { "kind": "conflict", "id": "Delta", "min": "3.0.0" } ] } ] },
              { "id": "Gamma", "type": "mod", "releases": [
                { "version": "2.0.0", "release_status": "stable", "dependencies": [ { "kind": "required", "id": "Unlisted" } ] },
                { "version": "1.0.0", "release_status": "stable" } ] },
              { "id": "Delta", "type": "mod", "releases": [
                { "version": "3.0.0", "release_status": "stable" },
                { "version": "2.0.0", "release_status": "stable", "dependencies": [ { "kind": "required", "id": "Beta", "min": "2.0.0" } ] },
                { "version": "1.0.0", "release_status": "stable" } ] },
              { "id": "Epsilon", "type": "mod", "releases": [
                { "version": "2.0.0", "release_status": "stable", "dependencies": [ { "kind": "conflict", "id": "Beta" } ] },
                { "version": "1.0.0", "release_status": "stable" } ] } ] }
            """)!.AsObject());
        ListingPackMember[] pins =
        [
            new("Alpha", "2.0.0"), new("Beta", "1.0.0"), new("Gamma", "1.0.0"), new("Delta", "1.0.0"), new("Epsilon", "1.0.0"),
        ];

        var fits = new[] { ("Beta", "2.0.0"), ("Gamma", "2.0.0"), ("Delta", "2.0.0"), ("Delta", "3.0.0"), ("Epsilon", "2.0.0"), ("Alpha", "1.0.0"), ("Beta", "1.0.0") }
            .Select(pin => ListingPackDependencies.Fits(snapshot, pins, new ListingPackMember(pin.Item1, pin.Item2)));

        Assert.Equal([false, false, false, false, false, true, true], fits);
    }

    [Fact]
    public void UnlistedNeeds_NamesTheRequiredModsThatAreNotListed_AndDefaultReleaseSkipsTheirReleases()
    {
        var snapshot = Snapshot(JsonNode.Parse("""
            { "listings": [
              { "id": "Alpha", "type": "mod", "releases": [
                { "version": "2.0.0", "release_status": "stable", "dependencies": [
                  { "kind": "required", "id": "Unlisted" }, { "kind": "required", "any_of": [ { "id": "Gone" }, { "id": "Other" } ] },
                  { "kind": "required", "any_of": [ { "id": "Gone" }, { "id": "Beta" } ] }, { "kind": "optional", "id": "Optional" } ] },
                { "version": "1.1.0", "release_status": "testing" },
                { "version": "1.0.0", "release_status": "stable" } ] },
              { "id": "Beta", "type": "mod", "releases": [ { "version": "1.0.0", "release_status": "stable" } ] } ] }
            """)!.AsObject());
        var alpha = snapshot.Listings[0];

        Assert.Equal(["Unlisted", "Gone Other"], ListingPackDependencies.UnlistedNeeds(snapshot, alpha.Releases[0]).Select(ids => string.Join(' ', ids)));
        Assert.Empty(ListingPackDependencies.UnlistedNeeds(snapshot, alpha.Releases[2]));
        Assert.Equal("1.0.0", ListingPackDependencies.DefaultRelease(snapshot, alpha)!.Version.ToString());
    }

    private static JsonObject Vector(string name) => Vectors.Single(vector => (string)vector!["name"]! == name)!.AsObject();

    private static List<string> Lines(IEnumerable<ListingIssue> issues, ListingIssueSeverity severity) =>
        issues.Where(issue => issue.Severity == severity).Select(issue => $"{issue.Location}: {issue.Message}").ToList();

    private static List<string> Expected(JsonObject vector, string key) => vector[key]!.AsArray().Select(line => (string)line!).ToList();

    /// <summary>
    /// The snapshot of a case, built as a real one ships: a delisted listing is a tombstone with only its index_status, and a
    /// disputed listing ships whole with index_status.state set to disputed. Fields the rules do not read get fixed values.
    /// </summary>
    private static ContentIndexSnapshot Snapshot(JsonObject node)
    {
        var delisted = Ids(node["delisted"]);
        var disputed = Ids(node["disputed"]);
        var listings = new List<ContentIndexListing>();
        foreach (var entry in node["listings"]!.AsArray())
        {
            var id = (string)entry!["id"]!;
            if (delisted.Contains(id))
            {
                listings.Add(new ContentIndexListing(id, null, [], new IndexStatus(IndexStatusState.Delisted, "delisted")));
                continue;
            }

            var type = MetadataEnumMapper.ParseContentType((string)entry["type"]!);
            var links = new Dictionary<string, string> { ["forums"] = "https://forums.ahwoo.com/threads/example.1/" };
            var authored = new ModMetadata(1, id, "index", id, ["Example Author"], "Abstract.", "MIT", links, "2026.9.10.5438", type);
            var releases = entry["releases"]!.AsArray().Select(release => Release(id, type, release!.AsObject())).ToList();
            listings.Add(new ContentIndexListing(id, authored, releases, disputed.Contains(id) ? new IndexStatus(IndexStatusState.Disputed, "disputed") : null));
        }

        return new ContentIndexSnapshot(1, listings, [], null, []);
    }

    private static ModVersionMetadata Release(string id, ContentType type, JsonObject release)
    {
        var since = (string?)release["download"]?["unavailable_since"];
        var download = new DownloadInfo($"https://example.com/{id}.zip", new string('a', 64), 1, "application/zip",
            unavailableSince: since is null ? null : DateTimeOffset.Parse(since, System.Globalization.CultureInfo.InvariantCulture));
        var dependencies = release["dependencies"]?.AsArray().Select(dependency => Dependency(dependency!.AsObject())).ToList() ?? [];
        return new ModVersionMetadata(1, id, ModVersion.Parse((string)release["version"]!), MetadataEnumMapper.ParseReleaseStatus((string)release["release_status"]!),
            DateTimeOffset.UnixEpoch, "2026.9.10.5438", 5438, download, 1, dependencies, type, yanked: (bool?)release["yanked"] ?? false, yankedReason: (string?)release["yanked_reason"]);
    }

    private static ModDependency Dependency(JsonObject dependency)
    {
        var kind = MetadataEnumMapper.ParseKind((string)dependency["kind"]!);
        if (dependency["any_of"] is JsonArray alternatives)
        {
            return ModDependency.OfAlternatives(kind, alternatives.Select(alternative =>
            {
                var (id, min, max) = Bounds(alternative!.AsObject());
                return new ModDependencyAlternative(id, min, max);
            }).ToList());
        }

        var (modId, low, high) = Bounds(dependency);
        return new ModDependency(modId, kind, low, high);
    }

    /// <summary>The id and the bounds of a choice. A case writes each bound in full SemVer, as the stamper does, so a short bound fails here.</summary>
    private static (string Id, ModVersion? Min, ModVersion? Max) Bounds(JsonObject option)
    {
        var (min, max) = ((string?)option["min"], (string?)option["max"]);
        var low = min is null ? (ModVersion?)null : ModVersion.Parse(min);
        var high = max is null ? (ModVersion?)null : ModVersion.Parse(max);
        return ((string)option["id"]!, low, high);
    }

    private static HashSet<string> Ids(JsonNode? node) => node?.AsArray().Select(id => (string)id!).ToHashSet(StringComparer.Ordinal) ?? [];
}
