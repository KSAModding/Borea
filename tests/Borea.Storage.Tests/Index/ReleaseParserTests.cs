using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Storage.Index;
using System.Text.Json;

namespace Borea.Storage.Tests.Index;

public sealed class ReleaseParserTests
{
    private static JsonElement Release(string type, string install = """{ "derived": true, "folder": "Example Rover" }""") => JsonDocument.Parse($$"""
        {
            "spec_version": 1,
            "id": "example-rover",
            "type": "{{type}}",
            "version": "1.0.0",
            "version_scheme": "semver",
            "release_status": "stable",
            "release_date": "2026-10-20T12:00:00Z",
            "game_min": "2026.10.19.5638",
            "game_min_revision": 5638,
            "download": { "url": "https://example.com/rover.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
            "install": {{install}},
            "install_size": 2048,
            "dependencies": []
        }
        """).RootElement;

    private static ModMetadata Authored(ContentType type) => new(
        specVersion: 1,
        modId: "example-rover",
        source: "index",
        name: "Example Rover",
        authors: ["Author"],
        abstractText: "A rover.",
        license: "CC0-1.0",
        links: new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" },
        gameMin: "2026.10",
        type: type);

    [Theory]
    [InlineData("vehicle", ContentType.Vehicle)]
    [InlineData("save", ContentType.Save)]
    [InlineData("mod", ContentType.Mod)]
    [InlineData("mod-loader", ContentType.ModLoader)]
    [InlineData("mod", ContentType.ModLoader)]
    public void Parse_ATypeThatFitsTheListing_IsValid(string releaseType, ContentType listingType)
    {
        var install = releaseType == "mod-loader" ? """{ "derived": true, "target": "standalone" }""" : """{ "derived": true, "folder": "Example Rover" }""";

        var outcome = ReleaseParser.Parse(Release(releaseType, install), "example-rover", "index", Authored(listingType));

        Assert.Equal(ParseOutcomeKind.Valid, outcome.Kind);
    }

    [Theory]
    [InlineData("vehicle", ContentType.Mod)]
    [InlineData("mod", ContentType.Vehicle)]
    [InlineData("save", ContentType.Vehicle)]
    [InlineData("mod-loader", ContentType.Save)]
    public void Parse_AVehicleOrSaveOnEitherSideThatDoesNotFit_IsRejectedAlone(string releaseType, ContentType listingType)
    {
        var outcome = ReleaseParser.Parse(Release(releaseType), "example-rover", "index", Authored(listingType));

        Assert.Equal(ParseOutcomeKind.Malformed, outcome.Kind);
        Assert.Contains("does not agree with the type of listing", outcome.Malformed!.Reason, StringComparison.Ordinal);
        Assert.Equal("1.0.0", outcome.Malformed.Version);
    }

    [Fact]
    public void Parse_AVehicleReleaseWithoutAuthoredListing_IsRejected()
    {
        var outcome = ReleaseParser.Parse(Release("vehicle"), "example-rover", "index", authored: null);

        Assert.Equal(ParseOutcomeKind.Malformed, outcome.Kind);
    }

    [Fact]
    public void Parse_AModReleaseWithoutAuthoredListing_KeepsItsVerdict()
    {
        var outcome = ReleaseParser.Parse(Release("mod"), "example-rover", "index", authored: null);

        Assert.Equal(ParseOutcomeKind.Valid, outcome.Kind);
    }
}
