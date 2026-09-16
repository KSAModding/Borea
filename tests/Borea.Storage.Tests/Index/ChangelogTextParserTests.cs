using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Storage.Index;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Index;

public sealed class ChangelogTextParserTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public ChangelogTextParserTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
    }

    [Fact]
    public void Parse_ChangelogText_ReadsItOnTheRelease()
    {
        var result = SnapshotParser.Parse(Snapshot(Release("1.0.0", "\"## Changes\\n- Fixes the HUD.\"")));

        var listing = Assert.Single(result.ValidListings);
        var release = Assert.Single(listing.ValidReleases);
        Assert.Equal("## Changes\n- Fixes the HUD.", release.ChangelogText);
        Assert.Equal("https://example.com/changelog/1.0.0", release.Changelog);
        Assert.Empty(listing.ChangelogTextErrors);
    }

    [Fact]
    public void Parse_NoChangelogText_LeavesItNull()
    {
        var result = SnapshotParser.Parse(Snapshot(Release("1.0.0", null)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Null(Assert.Single(listing.ValidReleases).ChangelogText);
        Assert.Empty(listing.ChangelogTextErrors);
    }

    [Fact]
    public void Parse_ChangelogTextOfExactlyTheLimit_IsKept()
    {
        var text = new string('a', ModVersionMetadata.MaxChangelogTextBytes);

        var result = SnapshotParser.Parse(Snapshot(Release("1.0.0", $"\"{text}\"")));

        var listing = Assert.Single(result.ValidListings);
        Assert.Equal(text, Assert.Single(listing.ValidReleases).ChangelogText);
        Assert.Empty(listing.ChangelogTextErrors);
    }

    [Theory]
    [InlineData(ModVersionMetadata.MaxChangelogTextBytes + 1, 'a', "has 16385 bytes of UTF-8, more than 16384")]
    [InlineData(ModVersionMetadata.MaxChangelogTextBytes / 2 + 1, '\u00e4', "has 16386 bytes of UTF-8, more than 16384")]
    public void Parse_ChangelogTextLongerThanTheLimit_DropsOnlyTheText(int length, char character, string reason)
    {
        var result = SnapshotParser.Parse(Snapshot(Release("1.0.0", $"\"{new string(character, length)}\"")));

        AssertOnlyTheTextIsDropped(result, reason);
    }

    [Theory]
    [InlineData("12", "must be a string, but was Number")]
    [InlineData("null", "must be a string, but was Null")]
    [InlineData("[\"- Fixes the HUD.\"]", "must be a string, but was Array")]
    [InlineData("{ \"text\": \"- Fixes the HUD.\" }", "must be a string, but was Object")]
    [InlineData("\"\\ud800\"", "is not valid Unicode text")]
    public void Parse_UnusableChangelogText_DropsOnlyTheText(string rawValue, string reason)
    {
        var result = SnapshotParser.Parse(Snapshot(Release("1.0.0", rawValue)));

        AssertOnlyTheTextIsDropped(result, reason);
    }

    [Fact]
    public void Parse_RejectedRelease_AddsNoChangelogTextError()
    {
        var result = SnapshotParser.Parse(Snapshot(Release("not-a-version", "12"), Release("1.0.0", null)));

        var listing = Assert.Single(result.ValidListings);
        Assert.Single(listing.ValidReleases);
        Assert.Single(listing.RejectedReleases);
        Assert.Empty(listing.ChangelogTextErrors);
    }

    [Fact]
    public async Task ReadAsync_UnusableChangelogText_KeepsTheReleaseAndAddsADiagnostic()
    {
        await WriteIndexAsync(Snapshot(Release("1.0.0", "12")));

        var snapshot = await new ContentIndexReader(_paths).ReadAsync();

        Assert.Null(Assert.Single(Assert.Single(snapshot.Listings).Releases).ChangelogText);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(ContentIndexDiagnosticKind.Malformed, diagnostic.Kind);
        Assert.Equal(ContentIndexDiagnosticScope.ChangelogText, diagnostic.Scope);
        Assert.Equal("test-mod", diagnostic.Id);
        Assert.Equal("1.0.0", diagnostic.Version);
    }

    private static void AssertOnlyTheTextIsDropped(IndexValidationResult result, string reason)
    {
        var listing = Assert.Single(result.ValidListings);
        var release = Assert.Single(listing.ValidReleases);
        Assert.Null(release.ChangelogText);
        Assert.Equal("https://example.com/changelog/1.0.0", release.Changelog);
        var error = Assert.Single(listing.ChangelogTextErrors);
        Assert.Equal("test-mod", error.Id);
        Assert.Equal("1.0.0", error.Version);
        Assert.Contains("The changelog_text value", error.Reason);
        Assert.Contains(reason, error.Reason);
    }

    private static string Release(string version, string? changelogText)
    {
        var changelogTextJson = changelogText is null ? string.Empty : $"\"changelog_text\": {changelogText},";
        return $$"""
            {
                "spec_version": 1,
                "id": "test-mod",
                "type": "mod",
                "version": "{{version}}",
                "version_scheme": "semver",
                "release_status": "stable",
                "release_date": "2026-08-08T12:00:00Z",
                "game_min": "2026.7.4.2131",
                "game_min_revision": 2131,
                "download": { "url": "https://example.com/mod.zip", "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "size": 1024, "content_type": "application/zip" },
                "install_size": 2048,
                "dependencies": [],
                {{changelogTextJson}}
                "changelog": "https://example.com/changelog/{{version}}"
            }
            """;
    }

    private static string Snapshot(params string[] releases) => $$"""
        {
            "snapshot_version": 1,
            "listings": [{
                "id": "test-mod",
                "authored": {
                    "spec_version": 1,
                    "id": "test-mod",
                    "type": "mod",
                    "name": "Test Mod",
                    "authors": ["Test Author"],
                    "abstract": "A mod used for testing.",
                    "license": "MIT",
                    "compatibility": { "game_min": "2026.7.4.2131" },
                    "links": { "forums": "https://forums.example/thread/1" }
                },
                "releases": [{{string.Join(",", releases)}}]
            }],
            "packs": [],
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
