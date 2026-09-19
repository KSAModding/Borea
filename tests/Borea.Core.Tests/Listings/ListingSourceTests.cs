using System.Buffers.Binary;
using System.Text;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Tags;

namespace Borea.Core.Tests.Listings;

public sealed class ListingSourceTests
{
    [Theory]
    [InlineData("Maximilian-Nesslauer/KSA-DeltaVMap", "Maximilian-Nesslauer", "KSA-DeltaVMap")]
    [InlineData(" https://github.com/StarMapLoader/StarMap ", "StarMapLoader", "StarMap")]
    [InlineData("https://github.com/StarMapLoader/StarMap/releases/tag/v0.4.6", "StarMapLoader", "StarMap")]
    [InlineData("github.com/owner/repo.git", "owner", "repo")]
    public void TryParse_GitHubRepository_NamesOwnerAndRepository(string text, string owner, string repository)
    {
        Assert.True(ListingSourceReference.TryParse(text, out var reference));
        Assert.Equal(new ListingSourceReference.GitHub(owner, repository), reference);
    }

    [Theory]
    [InlineData("4253")]
    [InlineData("https://spacedock.info/mod/4253/AdvancedFlightComputer")]
    [InlineData("spacedock.info/mod/4253")]
    public void TryParse_SpaceDockMod_NamesItsId(string text)
    {
        Assert.True(ListingSourceReference.TryParse(text, out var reference));
        Assert.Equal(new ListingSourceReference.SpaceDock(4253), reference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a source")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://spacedock.info/mods")]
    [InlineData("0")]
    public void TryParse_AnythingElse_Fails(string text) =>
        Assert.False(ListingSourceReference.TryParse(text, out _));

    [Fact]
    public void Apply_HostAndCodeModArchive_FillTheDraftAndProposeStarMap()
    {
        var host = new ListingHostFacts(
            new ListingSourceReference.GitHub("owner", "KSA-MyMod"),
            "KSA-MyMod",
            "Does a thing.",
            "MIT",
            ["owner"],
            [new ListingLink("forums", "https://forums.ahwoo.com/threads/my-mod.9/"), new ListingLink("repository", "https://github.com/owner/KSA-MyMod")],
            new ListingReleases("owner/KSA-MyMod", null),
            new ListingHostRelease("v1.0.0", "1.0.0", "https://example.com/MyMod.zip", 100, []));
        var archive = new ListingArchiveFacts("MyMod", ["MyMod"], ModToml: true, EntryAssembly: "MyMod", IsCodeMod: true);

        var draft = ListingPrefill.Apply(new ListingDraft(), new ListingSource(host, archive, null), Snapshot(), GameVersion.Parse("2026.9.10.5438"));

        Assert.Equal("MyMod", draft.Id);
        Assert.Equal("KSA-MyMod", draft.Name);
        Assert.Equal(["owner"], draft.Authors);
        Assert.Equal("Does a thing.", draft.Abstract);
        Assert.Equal("MIT", draft.License);
        Assert.Equal("https://forums.ahwoo.com/threads/my-mod.9/", draft.LinkOf("forums"));
        Assert.Equal("owner/KSA-MyMod", draft.Releases!.GitHub);
        Assert.Equal(new ListingLoader("StarMap", "0.4.6"), draft.Loader);
        Assert.Equal("2026.9.10.5438", draft.GameMin);
    }

    [Fact]
    public void Apply_ArchiveWithoutCodeOrRoot_KeepsTheIdAndAddsNoLoader()
    {
        var host = new ListingHostFacts(new ListingSourceReference.SpaceDock(5), null, null, null, [], [], new ListingReleases(null, 5), null);
        var archive = new ListingArchiveFacts(null, ["A", "B"], ModToml: false, EntryAssembly: null, IsCodeMod: false);

        var draft = ListingPrefill.Apply(new ListingDraft { Id = "Typed", Name = "Kept" }, new ListingSource(host, archive, null), null, null);

        Assert.Equal("Typed", draft.Id);
        Assert.Equal("Kept", draft.Name);
        Assert.Null(draft.Loader);
        Assert.Equal(5, draft.Releases!.SpaceDock);
        Assert.Equal(string.Empty, draft.GameMin);
    }

    [Fact]
    public void DefaultGameMin_WithoutAnInstalledGame_IsTheNewestSnapshotVersion()
    {
        Assert.Equal("2026.9.7.5402", ListingPrefill.DefaultGameMin(null, Snapshot()));
        Assert.Equal("2026.9.10.5438", ListingPrefill.DefaultGameMin(GameVersion.Parse("2026.9.10.5438-test"), Snapshot()));
        Assert.Null(ListingPrefill.DefaultGameMin(null, null));
    }

    [Fact]
    public void NewestStableVersion_SkipsTestingAndYankedReleases()
    {
        Assert.Equal("0.4.6", ListingPrefill.NewestStableVersion(Snapshot(), "starmap"));
        Assert.Null(ListingPrefill.NewestStableVersion(Snapshot(), "Unknown"));
    }

    [Fact]
    public void TagsFor_MapsForumPrefixesToCuratedTags()
    {
        var vocabulary = new CuratedTagVocabulary(1,
        [
            new CuratedTag("gameplay", "Gameplay", "Mechanics.", "Gameplay"),
            new CuratedTag("user-interface", "User Interface", "Windows.", "User Interface"),
            new CuratedTag("library", "Library", "Code."),
        ]);

        Assert.Equal(["user-interface"], ListingPrefill.TagsFor(["User Interface"], vocabulary));
        Assert.Empty(ListingPrefill.TagsFor(["Unknown"], vocabulary));
        Assert.Empty(ListingPrefill.TagsFor([], vocabulary));
    }

    [Fact]
    public void NewFile_ShortDocument_CarriesTheText()
    {
        var page = ListingPullRequestLinks.NewFile("MyMod", "id = \"MyMod\"\n");

        Assert.True(page.CarriesText);
        Assert.Equal("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml&value=id%20%3D%20%22MyMod%22%0A", page.Url.AbsoluteUri);
    }

    [Fact]
    public void NewFile_DocumentLongerThanTheLimit_OpensThePageWithoutTheText()
    {
        var page = ListingPullRequestLinks.NewFile("MyMod", new string('x', ListingPullRequestLinks.MaxUrlLength));

        Assert.False(page.CarriesText);
        Assert.Equal("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml", page.Url.AbsoluteUri);
    }

    [Fact]
    public void Edit_OpensTheEditPageOfTheListedFile()
    {
        var page = ListingPullRequestLinks.Edit("StarMap");

        Assert.False(page.CarriesText);
        Assert.Equal("https://github.com/KSAModding/content-index/edit/main/listings/StarMap.toml", page.Url.AbsoluteUri);
    }

    [Fact]
    public void Scan_FindsImagesOutsideCode()
    {
        const string markdown = """
            ![Map](ksa-image:map)
            ![Shot][shot] and ![inline](<https://example.com/a.png> "title")

            ```
            ![code](ksa-image:inside-code)
            ```

            `![span](ksa-image:inside-span)` and \![escaped](ksa-image:escaped)

            <img src="x.png"> <IMG src="y.png">

            [shot]: ksa-image:shot
            """;

        var (destinations, html) = MarkdownImages.Scan(markdown);

        Assert.Equal(["ksa-image:map", "ksa-image:shot", "https://example.com/a.png"], destinations);
        Assert.Equal(2, html);
        Assert.Equal(["map", "shot"], MarkdownImages.References(markdown));
    }

    [Fact]
    public void Scan_ReadsDestinationsAsCommonMarkDoes()
    {
        const string markdown = """
            A stray ` backtick.

            ![a [nested [twice]] alt](ksa-image&#58;map) ![escaped](ksa-image:my\_shot)

            Another ` backtick, and < img src="x.png"> is text.
            """;

        var (destinations, html) = MarkdownImages.Scan(markdown);

        Assert.Equal(["ksa-image:map", "ksa-image:my_shot"], destinations);
        Assert.Equal(0, html);
    }

    [Fact]
    public void Measure_PngIcon_GivesTheRecordFacts()
    {
        var bytes = Png(512, 512);

        var measurement = ListingImageMeasurement.Of(bytes, ListingImageRole.Icon);

        Assert.True(measurement.IsMeasured, measurement.Problem);
        Assert.Equal(512, measurement.Width);
        Assert.Equal(512, measurement.Height);
        Assert.Equal(bytes.Length, measurement.Size);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), measurement.Sha256);
        Assert.Equal("PNG", measurement.Format);
    }

    [Theory]
    [InlineData(200, 200, ListingImageRole.Icon, "outside the limits")]
    [InlineData(1024, 256, ListingImageRole.Icon, "outside the limits")]
    [InlineData(4096, 100, ListingImageRole.Description, "outside 1 to 2048 per side")]
    public void Measure_SizeOutsideTheRole_FailsWithTheLimit(int width, int height, ListingImageRole role, string expected)
    {
        var measurement = ListingImageMeasurement.Of(Png(width, height), role);

        Assert.False(measurement.IsMeasured);
        Assert.Contains(expected, measurement.Problem);
    }

    [Fact]
    public void Measure_AnimatedOrUnreadableBytes_Fail()
    {
        Assert.Equal("the PNG is animated", ListingImageMeasurement.Of(Png(512, 512, animated: true), ListingImageRole.Icon).Problem);
        Assert.Equal("the bytes are not PNG, JPEG or WebP", ListingImageMeasurement.Of("GIF89a"u8, ListingImageRole.Description).Problem);
        Assert.Contains("above the cap", ListingImageMeasurement.Of(new byte[(int)IconImage.MaxBytes + 1], ListingImageRole.Icon).Problem);
    }

    private static ContentIndexSnapshot Snapshot()
    {
        var starMap = new ContentIndexListing("StarMap", null,
        [
            Release("StarMap", "0.4.5", ReleaseStatus.Stable),
            Release("StarMap", "0.4.6", ReleaseStatus.Stable),
            Release("StarMap", "0.5.0-beta", ReleaseStatus.Testing),
            Release("StarMap", "0.4.7", ReleaseStatus.Stable, yanked: true),
        ], null);
        return new ContentIndexSnapshot(1, [starMap], [], new ContentIndexGameVersions(1, "test", ["2026.8.19.5261", "2026.9.7.5402", "2026.9.4.5400"]), []);
    }

    private static ModVersionMetadata Release(string id, string version, ReleaseStatus status, bool yanked = false) => new(
        specVersion: 1,
        modId: id,
        version: ModVersion.Parse(version),
        releaseStatus: status,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo("https://example.com/a.zip", null, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>(),
        yanked: yanked,
        yankedReason: yanked ? "broken" : null);

    private static byte[] Png(int width, int height, bool animated = false)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        List<byte> bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        AddPngChunk(bytes, "IHDR", header);
        if (animated)
            AddPngChunk(bytes, "acTL", [0, 0, 0, 2, 0, 0, 0, 0]);
        AddPngChunk(bytes, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01]);
        AddPngChunk(bytes, "IEND", []);
        return [.. bytes];
    }

    private static void AddPngChunk(List<byte> bytes, string kind, byte[] body)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        bytes.AddRange(length);
        bytes.AddRange(Encoding.ASCII.GetBytes(kind));
        bytes.AddRange(body);
        bytes.AddRange(new byte[4]);
    }
}
