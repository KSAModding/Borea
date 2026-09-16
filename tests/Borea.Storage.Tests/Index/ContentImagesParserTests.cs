using Borea.Core.Index;
using Borea.Storage.Index;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Index;

public sealed class ContentImagesParserTests : IDisposable
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public ContentImagesParserTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
    }

    [Fact]
    public void Parse_IconAndDescriptionImages_ReadsEveryRecord()
    {
        var icon = """
            {
                "url": "https://example.com/icon.png",
                "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "width": 512,
                "height": 512,
                "size": 48213,
                "license": "CC-BY-4.0",
                "attribution": "Artwork by Example Artist",
                "source": "https://example.com/original"
            }
            """;
        var images = $$"""{ "icon": {{icon}}, "description": [{{Record("settings-window")}}, {{Record("map-view")}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Empty(listing.ImagesErrors);
        var parsedIcon = listing.Images!.Icon!;
        Assert.Equal("https://example.com/icon.png", parsedIcon.Url);
        Assert.Equal(Digest.ToUpperInvariant(), parsedIcon.Sha256);
        Assert.Equal(512, parsedIcon.Width);
        Assert.Equal(512, parsedIcon.Height);
        Assert.Equal(48213, parsedIcon.SizeBytes);
        Assert.Equal("CC-BY-4.0", parsedIcon.License);
        Assert.Equal("Artwork by Example Artist", parsedIcon.Attribution);
        Assert.Equal("https://example.com/original", parsedIcon.Source);
        Assert.Equal(["settings-window", "map-view"], listing.Images.Description.Select(image => image.Id));
        Assert.Null(listing.Images.FindDescriptionImage("map-view")!.License);
    }

    [Fact]
    public void Parse_NoImages_LeavesImagesNull()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", null)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.Images);
        Assert.Empty(listing.ImagesErrors);
    }

    [Fact]
    public void Parse_EmptyImages_LeavesImagesNull()
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", "{}")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.Images);
        Assert.Empty(listing.ImagesErrors);
    }

    [Fact]
    public void Parse_OnlyDescriptionImages_HasNoIcon()
    {
        var images = $$"""{ "description": [{{Record("shot")}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var parsed = Assert.Single(result.ValidListings).Images!;
        Assert.Null(parsed.Icon);
        Assert.Equal("shot", Assert.Single(parsed.Description).Id);
    }

    [Fact]
    public void Parse_MalformedRecordBesideAValidOne_DropsOnlyTheBadRecord()
    {
        var bad = Record("settings-window", "url", "\"http://example.com/image.png\"");
        var images = $$"""{ "icon": {{Record(null)}}, "description": [{{bad}}, {{Record("map-view")}}] }""";
        var listings = Listing("test-mod", images) + "," + Listing("other-mod", images.Replace(bad, Record("shot"), StringComparison.Ordinal));

        var result = SnapshotParser.Parse(Snapshot(listings));

        Assert.Empty(result.MalformedListings);
        var listing = result.ValidListings.Single(entry => entry.Id == "test-mod");
        Assert.NotNull(listing.Authored);
        Assert.Single(listing.ValidReleases);
        Assert.NotNull(listing.Images!.Icon);
        Assert.Equal("map-view", Assert.Single(listing.Images.Description).Id);
        var error = Assert.Single(listing.ImagesErrors);
        Assert.Equal("test-mod", error.Id);
        Assert.Null(error.Version);
        Assert.Contains("The images description item at index 0 is unreadable.", error.Reason);
        Assert.Contains("HTTPS", error.Reason);
        var sibling = result.ValidListings.Single(entry => entry.Id == "other-mod");
        Assert.Equal(2, sibling.Images!.Description.Count);
        Assert.Empty(sibling.ImagesErrors);
    }

    [Theory]
    [InlineData("id", null, "must contain a string id")]
    [InlineData("id", "\"-shot\"", "must be 1 to 64 ASCII letters")]
    [InlineData("url", null, "must contain a string url")]
    [InlineData("sha256", "\"ABC\"", "64 hex characters")]
    [InlineData("width", "\"512\"", "width value must be a whole number")]
    [InlineData("height", "1.5", "height value must be a whole number")]
    [InlineData("size", null, "size value must be a whole number")]
    [InlineData("size", "0", "must be positive")]
    [InlineData("width", "4096", "at most 2048 pixels")]
    [InlineData("size", "1048577", "at most 1048576 bytes")]
    [InlineData("license", "5", "license value must be a string")]
    [InlineData("license", "\"\"", "license cannot be empty")]
    [InlineData("source", "\"http://example.com/original\"", "source must be an absolute HTTPS URL")]
    public void Parse_InvalidDescriptionRecord_DropsOnlyThatRecord(string key, string? rawValue, string reason)
    {
        var images = $$"""{ "description": [{{Record("keep")}}, {{Record("shot", key, rawValue)}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal("keep", Assert.Single(listing.Images!.Description).Id);
        var error = Assert.Single(listing.ImagesErrors);
        Assert.Contains("The images description item at index 1 is unreadable.", error.Reason);
        Assert.Contains(reason, error.Reason);
    }

    [Fact]
    public void Parse_DescriptionRecordThatIsNotAnObject_DropsOnlyThatRecord()
    {
        var images = $$"""{ "description": [7, {{Record("keep")}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal("keep", Assert.Single(listing.Images!.Description).Id);
        Assert.Contains("item at index 0 must be an object", Assert.Single(listing.ImagesErrors).Reason);
    }

    [Theory]
    [InlineData("width", "2049", "longer side of the icon can be at most 2 times the shorter side")]
    [InlineData("size", "300000", "at most 262144 bytes")]
    [InlineData("url", "\"http://example.com/icon.png\"", "HTTPS")]
    [InlineData("sha256", null, "must contain a string sha256")]
    public void Parse_InvalidIcon_KeepsTheDescriptionImages(string key, string? rawValue, string reason)
    {
        var images = $$"""{ "icon": {{Record(null, key, rawValue)}}, "description": [{{Record("shot")}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.Images!.Icon);
        Assert.Single(listing.Images.Description);
        var error = Assert.Single(listing.ImagesErrors);
        Assert.Contains("The images icon is unreadable.", error.Reason);
        Assert.Contains(reason, error.Reason);
    }

    [Theory]
    [InlineData("[]", "Array")]
    [InlineData("\"icon.png\"", "String")]
    [InlineData("null", "Null")]
    public void Parse_ImagesNotAnObject_DropsAllImagesAndKeepsTheListing(string images, string kind)
    {
        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.NotNull(listing.Authored);
        Assert.Single(listing.ValidReleases);
        Assert.Null(listing.Images);
        var error = Assert.Single(listing.ImagesErrors);
        Assert.Contains("The images value is unreadable.", error.Reason);
        Assert.Contains($"must be an object, but was {kind}", error.Reason);
    }

    [Fact]
    public void Parse_DescriptionNotAnArray_KeepsTheIcon()
    {
        var images = $$"""{ "icon": {{Record(null)}}, "description": {{Record("shot")}} }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.NotNull(listing.Images!.Icon);
        Assert.Empty(listing.Images.Description);
        Assert.Contains("must be an array", Assert.Single(listing.ImagesErrors).Reason);
    }

    [Fact]
    public void Parse_DuplicateDescriptionId_DropsEveryRecordOfThatId()
    {
        var images = $$"""{ "description": [{{Record("shot")}}, {{Record("map")}}, {{Record("shot")}}, {{Record("Shot")}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(["map", "Shot"], listing.Images!.Description.Select(image => image.Id));
        Assert.Contains("'shot' appears more than once", Assert.Single(listing.ImagesErrors).Reason);
    }

    [Fact]
    public void Parse_DuplicateDescriptionIdOnAnInvalidRecord_StillDropsTheValidRecord()
    {
        var bad = Record("shot", "url", "\"http://example.com/image.png\"");
        var images = $$"""{ "description": [{{Record("shot")}}, {{bad}}, {{Record("map")}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal("map", Assert.Single(listing.Images!.Description).Id);
        Assert.Equal(2, listing.ImagesErrors.Count);
        Assert.Contains(listing.ImagesErrors, error => error.Reason.Contains("index 1 is unreadable", StringComparison.Ordinal));
        Assert.Contains(listing.ImagesErrors, error => error.Reason.Contains("'shot' appears more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MoreThanSixteenDescriptionImages_DropsEveryDescriptionImage()
    {
        var records = string.Join(",", Enumerable.Range(0, 17).Select(index => Record($"shot-{index}")));
        var images = $$"""{ "icon": {{Record(null)}}, "description": [{{records}}] }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.NotNull(listing.Images!.Icon);
        Assert.Empty(listing.Images.Description);
        Assert.Contains("has 17 images, more than 16", Assert.Single(listing.ImagesErrors).Reason);
    }

    [Fact]
    public void Parse_UnknownRoleAndRecordKey_AreIgnored()
    {
        var images = $$"""{ "icon": {{Record(null, "future_key", "true")}}, "banner": {{Record(null)}} }""";

        var result = SnapshotParser.Parse(Snapshot(Listing("test-mod", images)));

        var listing = Assert.Single(result.ValidListings);
        Assert.NotNull(listing.Images!.Icon);
        Assert.Empty(listing.Images.Description);
        Assert.Empty(listing.ImagesErrors);
    }

    [Fact]
    public void Parse_Tombstone_CarriesNoImages()
    {
        var tombstone = """{ "id": "removed-mod", "index_status": { "state": "delisted" } }""";

        var result = SnapshotParser.Parse(Snapshot(tombstone));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(listing.Authored);
        Assert.Null(listing.Images);
        Assert.Empty(listing.ImagesErrors);
    }

    [Fact]
    public void Parse_PackVersionImages_AttachToEachVersion()
    {
        var withIcon = PackVersion("2.0.0", $$"""{ "icon": {{Record(null)}} }""");
        var withBadIcon = PackVersion("1.0.0", $$"""{ "icon": {{Record(null, "width", "128")}} }""");

        var result = SnapshotParser.Parse(Snapshot("", Pack(withIcon, withBadIcon)));

        var pack = Assert.Single(result.ValidPacks);
        Assert.Equal(2, pack.ValidVersions.Count);
        var newest = pack.ValidVersions.Single(version => version.Metadata.Version.ToString() == "2.0.0");
        Assert.NotNull(newest.Images!.Icon);
        Assert.Empty(newest.ImagesErrors);
        var older = pack.ValidVersions.Single(version => version.Metadata.Version.ToString() == "1.0.0");
        Assert.Null(older.Images);
        var error = Assert.Single(older.ImagesErrors);
        Assert.Equal("test-pack", error.Id);
        Assert.Equal("1.0.0", error.Version);
    }

    [Fact]
    public async Task ReadAsync_Images_AttachToTheListingAndThePackVersion()
    {
        var images = $$"""{ "icon": {{Record(null)}}, "description": [{{Record("shot")}}] }""";
        await WriteIndexAsync(Snapshot(Listing("test-mod", images), Pack(PackVersion("1.0.0", images))));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        var listing = Assert.Single(snapshot.Listings);
        Assert.NotNull(listing.Images!.Icon);
        Assert.Equal("shot", Assert.Single(listing.Images.Description).Id);
        Assert.NotNull(Assert.Single(Assert.Single(snapshot.Packs).Versions).Images!.Icon);
        Assert.Empty(snapshot.Diagnostics);
    }

    [Fact]
    public async Task ReadAsync_MalformedImages_KeepTheContentAndAddDiagnostics()
    {
        var badListingImages = $$"""{ "description": [{{Record("shot", "sha256", "\"ABC\"")}}] }""";
        var badPackImages = """{ "icon": 12 }""";
        await WriteIndexAsync(Snapshot(Listing("test-mod", badListingImages), Pack(PackVersion("1.0.0", badPackImages))));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        var listing = Assert.Single(snapshot.Listings);
        Assert.Null(listing.Images);
        Assert.Single(listing.Releases);
        Assert.Single(Assert.Single(snapshot.Packs).Versions);
        Assert.Equal(2, snapshot.Diagnostics.Count);
        Assert.All(snapshot.Diagnostics, diagnostic =>
        {
            Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
            Assert.Equal(ContentIndexDiagnosticScope.Images, diagnostic.Scope);
        });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Id == "test-mod" && diagnostic.Version is null);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Id == "test-pack" && diagnostic.Version == "1.0.0");
    }

    private static string Record(string? id, string? key = null, string? rawValue = null)
    {
        var fields = new List<(string Key, string Value)>
        {
            ("url", "\"https://example.com/image.png\""),
            ("sha256", $"\"{Digest}\""),
            ("width", "512"),
            ("height", "512"),
            ("size", "1000"),
        };
        if (id is not null)
            fields.Insert(0, ("id", $"\"{id}\""));

        if (key is not null)
        {
            fields.RemoveAll(field => field.Key == key);
            if (rawValue is not null)
                fields.Add((key, rawValue));
        }

        return "{ " + string.Join(", ", fields.Select(field => $"\"{field.Key}\": {field.Value}")) + " }";
    }

    private static string Listing(string id, string? images)
    {
        var imagesJson = images is null ? string.Empty : $", \"images\": {images}";
        return $$"""
            {
                "id": "{{id}}",
                "authored": {
                    "spec_version": 1,
                    "id": "{{id}}",
                    "type": "mod",
                    "name": "Test Mod",
                    "authors": ["Test Author"],
                    "abstract": "A mod used for testing.",
                    "license": "MIT",
                    "compatibility": { "game_min": "2026.7.4.2131" },
                    "links": { "forums": "https://forums.example/thread/1" }{{imagesJson}}
                },
                "releases": [{
                    "spec_version": 1,
                    "id": "{{id}}",
                    "type": "mod",
                    "version": "1.0.0",
                    "version_scheme": "semver",
                    "release_status": "stable",
                    "release_date": "2026-08-08T12:00:00Z",
                    "game_min": "2026.7.4.2131",
                    "game_min_revision": 2131,
                    "download": { "url": "https://example.com/mod.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
                    "install_size": 2048,
                    "dependencies": []
                }]
            }
            """;
    }

    private static string Pack(params string[] versions) => $$"""
        { "id": "test-pack", "versions": [{{string.Join(",", versions)}}] }
        """;

    private static string PackVersion(string version, string images) => $$"""
        {
            "authored": {
                "spec_version": 1,
                "id": "test-pack",
                "type": "modpack",
                "name": "Test Pack",
                "authors": ["Author"],
                "abstract": "Abstract.",
                "license": "CC0-1.0",
                "version": "{{version}}",
                "released_at": "2026-08-08T12:00:00Z",
                "links": { "forums": "https://forums.example/thread/1" },
                "compatibility": { "game_min": "2026.7" },
                "mods": [ { "id": "some-mod", "version": "1.0.0" } ],
                "images": {{images}}
            }
        }
        """;

    private static string Snapshot(string listings, string packs = "") => $$"""
        {
            "snapshot_version": 1,
            "listings": [{{listings}}],
            "packs": [{{packs}}],
            "game_versions": { "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }
        }
        """;

    private async Task WriteIndexAsync(string content)
    {
        var path = _paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
