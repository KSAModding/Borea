using Borea.Core.Index;
using Borea.Core.Stewardship;
using Borea.Core.Tests.ModPacks;

namespace Borea.Core.Tests.Stewardship;

public sealed class GoneListingTests
{
    private static readonly DateTimeOffset Early = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Late = new(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);

    [Fact]
    public void From_TakesOnlyTheReleasesWithTheMark_ByListingAndNewestMarkFirst()
    {
        var snapshot = Snapshot(
            ("MeasureTools", [("1.1.10", null), ("1.1.9", Early), ("1.1.8", Late), ("1.1.7", Late)]),
            ("StarMap", [("0.4.6", null)]),
            ("AdvancedFlightComputer", [("0.7.5", null), ("0.7.2", Early)]),
            ("KSArmory", [("0.8.44", Late)]));

        var gone = GoneListing.From(snapshot);

        Assert.Equal(["KSArmory", "MeasureTools", "AdvancedFlightComputer"], gone.Select(listing => listing.ListingId));
        Assert.Equal(["KSArmory name", "MeasureTools name", "AdvancedFlightComputer name"], gone.Select(listing => listing.Name));
        Assert.Equal(
            [("1.1.8", Late), ("1.1.7", Late), ("1.1.9", Early)],
            gone[1].Releases.Select(release => (release.Version.ToString(), release.Since)));
        Assert.Equal([("0.7.2", Early)], gone[2].Releases.Select(release => (release.Version.ToString(), release.Since)));
    }

    [Fact]
    public void From_ASnapshotWithoutTheMark_HasNoListing()
    {
        var snapshot = Snapshot(("MeasureTools", [("1.1.10", null), ("1.1.9", null)]), ("StarMap", [("0.4.6", null)]));

        Assert.Empty(GoneListing.From(snapshot));
    }

    [Fact]
    public void From_AListingWithoutAnAuthoredDocument_HasNoName()
    {
        var snapshot = new ContentIndexSnapshot(
            1,
            [new ContentIndexListing("MeasureTools", null, [PackMemberRepository.Release("MeasureTools", "1.1.9", unavailableSince: Early)], null)],
            [],
            null,
            []);

        var gone = Assert.Single(GoneListing.From(snapshot));

        Assert.Equal(("MeasureTools", null), (gone.ListingId, gone.Name));
    }

    private static ContentIndexSnapshot Snapshot(params (string Id, (string Version, DateTimeOffset? Since)[] Releases)[] listings) => new(
        1,
        [.. listings.Select(listing => new ContentIndexListing(
            listing.Id,
            PackMemberRepository.Listing(listing.Id, listing.Id + " name"),
            [.. listing.Releases.Select(release => PackMemberRepository.Release(listing.Id, release.Version, unavailableSince: release.Since))],
            null))],
        [],
        null,
        []);
}
