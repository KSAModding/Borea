using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Index;

public sealed class ContentIndexDatesTests
{
    private static readonly DateTimeOffset Published = new(2026, 8, 2, 13, 18, 41, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 9, 2, 10, 14, 5, TimeSpan.Zero);

    [Fact]
    public void Listing_BothDates_KeepsThem()
    {
        var listing = new ContentIndexListing("test-mod", null, Array.Empty<ModVersionMetadata>(), null, publishedAt: Published, updatedAt: Updated);

        Assert.Equal(Published, listing.PublishedAt);
        Assert.Equal(Updated, listing.UpdatedAt);
    }

    [Fact]
    public void Listing_OnlyPublishedAt_LeavesUpdatedAtNull()
    {
        var listing = new ContentIndexListing("test-mod", null, Array.Empty<ModVersionMetadata>(), null, publishedAt: Published);

        Assert.Equal(Published, listing.PublishedAt);
        Assert.Null(listing.UpdatedAt);
    }

    [Fact]
    public void Listing_UpdatedBeforePublished_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContentIndexListing("test-mod", null, Array.Empty<ModVersionMetadata>(), null, publishedAt: Updated, updatedAt: Published));
    }

    [Fact]
    public void Pack_UpdatedBeforePublished_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ContentIndexPack("test-pack", Array.Empty<ContentIndexPackVersion>(), null, Updated, Published));
    }

    [Fact]
    public void Pack_SameDate_IsValid()
    {
        var pack = new ContentIndexPack("test-pack", Array.Empty<ContentIndexPackVersion>(), null, Published, Published);

        Assert.Equal(pack.PublishedAt, pack.UpdatedAt);
    }
}
