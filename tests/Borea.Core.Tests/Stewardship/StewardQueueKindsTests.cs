using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class StewardQueueKindsTests
{
    private const string ContentIndex = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";

    [Fact]
    public void Of_TwoDocumentLabels_GivesBothKindsInTheirOrder()
    {
        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(ContentIndex, ["pack", "needs-steward", "listing", "area:publishing"], [new("listings/MyMod.toml", "added"), new("packs/my-pack/1.0.0.TOML", "modified")]);

        Assert.Equal([StewardQueueKind.Listing, StewardQueueKind.Pack], kinds);
        Assert.False(hasOtherFiles);
    }

    [Fact]
    public void Of_StewardFilesOfContentIndex_AddTheirKindsToTheLabels()
    {
        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(ContentIndex, ["pack"], Changed("tags.toml", "packs/my-pack/owner.json", "index-status.toml", "packs/my-pack/1.1.0.toml"));

        Assert.Equal([StewardQueueKind.Pack, StewardQueueKind.OwnerRecord, StewardQueueKind.IndexStatus, StewardQueueKind.TagVocabulary], kinds);
        Assert.False(hasOtherFiles);
    }

    [Fact]
    public void Of_FirstPackClaim_CountsItsOwnerRecordAsPartOfThePack()
    {
        StewardQueueFile[] claim = [new("packs/my-pack/1.0.0.toml", "added"), new("packs/my-pack/owner.json", "added")];

        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(ContentIndex, ["pack"], claim);

        Assert.Equal([StewardQueueKind.Pack], kinds);
        Assert.False(hasOtherFiles);
        Assert.Equal(["packs/my-pack/owner.json"], StewardQueueKinds.FirstClaims(claim));
    }

    /// <summary>The cases of claims in tools/check_scope.py of content-index: only an added record beside an added version of its own pack is a claim.</summary>
    [Theory]
    [InlineData("modified", "added", "packs/my-pack/1.1.0.toml")]
    [InlineData("removed", "added", "packs/my-pack/1.1.0.toml")]
    [InlineData("added", "modified", "packs/my-pack/1.0.0.toml")]
    [InlineData("added", "added", "packs/other-pack/1.0.0.toml")]
    [InlineData("added", "added", "packs/My-Pack/1.0.0.toml")]
    [InlineData("added", "added", "listings/my-pack.toml")]
    public void Of_HandoverOrAnyOtherOwnerRecordChange_IsAnOwnerRecordForASteward(string recordStatus, string documentStatus, string document)
    {
        StewardQueueFile[] files = [new(document, documentStatus), new("packs/my-pack/owner.json", recordStatus)];

        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(ContentIndex, [], files);

        Assert.Equal([StewardQueueKind.OwnerRecord], kinds);
        Assert.False(hasOtherFiles);
        Assert.Empty(StewardQueueKinds.FirstClaims(files));
    }

    [Theory]
    [InlineData("tools/decide.py")]
    [InlineData(".github/workflows/checks.yml")]
    [InlineData("schemas/listing.schema.json")]
    [InlineData("site/index.html")]
    [InlineData("listings/nested/MyMod.toml")]
    [InlineData("packs/my-pack/notes.md")]
    [InlineData("Tags.toml")]
    public void Of_AnyOtherFileOfContentIndex_IsReviewedOnGitHub(string path)
    {
        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(ContentIndex, ["listing"], Changed("listings/MyMod.toml", path));

        Assert.Equal([StewardQueueKind.Listing], kinds);
        Assert.True(hasOtherFiles);
    }

    [Fact]
    public void Of_Releases_TakesItsOwnLabelsAndKnowsOnlyReleaseFiles()
    {
        var release = StewardQueueKinds.Of(Releases, ["amendment", "release", "listing"], [new("releases/MyMod/1.0.0.json", "modified"), new("releases/MyMod/1.1.0.JSON", "added")]);
        var other = StewardQueueKinds.Of(Releases, ["amendment"], Changed("releases/MyMod/1.0.0.json", "index-status.toml"));
        var contentIndexLabels = StewardQueueKinds.Of(ContentIndex, ["release", "amendment"], Changed("listings/MyMod.toml"));

        Assert.Equal([StewardQueueKind.Release, StewardQueueKind.Amendment], release.Kinds);
        Assert.False(release.HasOtherFiles);
        Assert.Equal([StewardQueueKind.Amendment], other.Kinds);
        Assert.True(other.HasOtherFiles);
        Assert.Empty(contentIndexLabels.Kinds);
        Assert.False(contentIndexLabels.HasOtherFiles);
    }

    [Theory]
    [InlineData(Releases, "releases/MyMod/1.0.0.json", "removed", null)]
    [InlineData(Releases, "releases/MyMod/1.0.1.json", "renamed", "releases/MyMod/1.0.0.json")]
    [InlineData(ContentIndex, "listings/MyMod.toml", "removed", null)]
    [InlineData(ContentIndex, "packs/my-pack/1.0.1.toml", "renamed", "packs/my-pack/1.0.0.toml")]
    public void Of_RemovedOrRenamedDocument_IsReviewedOnGitHub(string repository, string path, string status, string? previousPath)
    {
        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(repository, ["needs-steward"], [new(path, status, previousPath)]);

        Assert.Empty(kinds);
        Assert.True(hasOtherFiles);
    }

    [Fact]
    public void Of_RenamedStewardFile_CountsItsOldPathToo()
    {
        var (kinds, hasOtherFiles) = StewardQueueKinds.Of(ContentIndex, [], [new("tags.toml", "renamed", "tools/tags.toml")]);

        Assert.Equal([StewardQueueKind.TagVocabulary], kinds);
        Assert.True(hasOtherFiles);
    }

    [Fact]
    public void Of_AnotherRepository_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => StewardQueueKinds.Of("KSAModding/Borea", [], []));
    }

    [Theory]
    [InlineData(ContentIndex, "listings/MyMod.toml", "added", StewardQueueKind.Listing)]
    [InlineData(ContentIndex, "listings/MyMod.TOML", "modified", StewardQueueKind.Listing)]
    [InlineData(ContentIndex, "packs/my-pack/1.0.0.toml", "modified", StewardQueueKind.Pack)]
    [InlineData(ContentIndex, "packs/my-pack/owner.json", "added", null)]
    [InlineData(ContentIndex, "listings/MyMod.toml", "removed", null)]
    [InlineData(ContentIndex, "listings/MyMod.toml", "renamed", null)]
    [InlineData(ContentIndex, "tags.toml", "modified", null)]
    [InlineData(ContentIndex, "listings/nested/MyMod.toml", "added", null)]
    [InlineData(Releases, "listings/MyMod.toml", "added", null)]
    [InlineData(Releases, "releases/MyMod/1.0.0.json", "modified", null)]
    public void DocumentOf_OnlyAWrittenListingOrPackOfContentIndex(string repository, string path, string status, StewardQueueKind? kind)
    {
        Assert.Equal(kind, StewardQueueKinds.DocumentOf(repository, path, status));
    }

    private static StewardQueueFile[] Changed(params string[] paths) => [.. paths.Select(path => new StewardQueueFile(path, "modified"))];
}
