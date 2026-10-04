using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using static Borea.Core.Tests.ModPacks.PackMemberRepository;

namespace Borea.Core.Tests.ModPacks;

public sealed class ModPackMemberReleasesTests
{
    [Fact]
    public void Find_MembersAtTheirNewestRelease_FindsNothing()
    {
        var snapshot = Snapshot(Release("A", "1.0.0"), Release("B", "2.0.0"), Release("B", "1.0.0"));

        Assert.Empty(ModPackMemberReleases.Find(Pack(("A", "1.0.0"), ("B", "2.0.0")), snapshot));
    }

    [Fact]
    public void Find_NamesTheNewerReleaseOfEachMember_InPackOrder_AndLeavesOutAnUnlistedPin()
    {
        var snapshot = Snapshot(Release("A", "1.1.0"), Release("A", "1.0.0"), Release("B", "3.0.0"), Release("B", "2.0.0"), Release("C", "1.0.0"));

        var newer = ModPackMemberReleases.Find(Pack(("B", "2.0.0"), ("Unlisted", "1.0.0"), ("C", "1.0.0"), ("A", "1.0.0")), snapshot);

        Assert.Equal([("B", "2.0.0", "3.0.0"), ("A", "1.0.0", "1.1.0")], newer.Select(member => (member.ModId, member.Pinned.ToString(), member.Newer.Version.ToString())));
    }

    [Fact]
    public void Find_StablePin_WhoseOnlyNewerReleasesAreTestingDevYankedOrGone_IsNotCounted()
    {
        var gone = new DateTimeOffset(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);
        var snapshot = Snapshot(
            Release("A", "1.3.0", unavailableSince: gone),
            Release("A", "1.2.0-dev.1", ReleaseStatus.Dev),
            Release("A", "1.1.0", yanked: true),
            Release("A", "1.1.0-beta.1", ReleaseStatus.Testing),
            Release("A", "1.0.0"));

        Assert.Empty(ModPackMemberReleases.Find(Pack(("A", "1.0.0")), snapshot));
    }

    [Fact]
    public void Find_TestingPin_CountsANewerTestingRelease()
    {
        var snapshot = Snapshot(Release("A", "1.2.0-dev.1", ReleaseStatus.Dev), Release("A", "1.1.0-beta.2", ReleaseStatus.Testing), Release("A", "1.1.0-beta.1", ReleaseStatus.Testing));

        var newer = ModPackMemberReleases.Find(Pack(("A", "1.1.0-beta.1")), snapshot);

        Assert.Equal("1.1.0-beta.2", Assert.Single(newer).Newer.Version.ToString());
    }

    [Fact]
    public void NewerRelease_StablePin_IsOutdatedOnlyByANewerStableReleaseThatIsNotYanked()
    {
        var snapshot = Snapshot(Release("A", "1.3.0-beta.1", ReleaseStatus.Testing), Release("A", "1.2.0", yanked: true), Release("A", "1.1.0"), Release("A", "1.0.1"), Release("A", "1.0.0"));

        Assert.Equal("1.1.0", ModPackMemberReleases.NewerRelease(snapshot, "a", ModVersion.Parse("1.0.0"))!.Version.ToString());
        Assert.Null(ModPackMemberReleases.NewerRelease(snapshot, "A", ModVersion.Parse("1.1.0")));
    }

    [Fact]
    public void NewerRelease_TestingPin_CountsANewerTestingOrStableRelease_AndNoDevRelease()
    {
        var snapshot = Snapshot(Release("A", "1.2.0-dev.1", ReleaseStatus.Dev), Release("A", "1.1.0-beta.2", ReleaseStatus.Testing), Release("A", "1.1.0-beta.1", ReleaseStatus.Testing));

        Assert.Equal("1.1.0-beta.2", ModPackMemberReleases.NewerRelease(snapshot, "A", ModVersion.Parse("1.1.0-beta.1"))!.Version.ToString());
    }

    [Fact]
    public void NewerRelease_PinOnAYankedTestingRelease_KeepsTheStabilityOfThatRelease()
    {
        var snapshot = Snapshot(Release("A", "1.1.0-beta.2", ReleaseStatus.Testing), Release("A", "1.1.0-beta.1", ReleaseStatus.Testing, yanked: true));

        Assert.Equal("1.1.0-beta.2", ModPackMemberReleases.NewerRelease(snapshot, "A", ModVersion.Parse("1.1.0-beta.1"))!.Version.ToString());
    }

    [Fact]
    public void NewerRelease_LeavesOutAReleaseWhoseDownloadIsGone()
    {
        var gone = new DateTimeOffset(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);
        var snapshot = Snapshot(Release("A", "1.2.0", unavailableSince: gone), Release("A", "1.1.0"), Release("A", "1.0.0"));

        Assert.Equal("1.1.0", ModPackMemberReleases.NewerRelease(snapshot, "A", ModVersion.Parse("1.0.0"))!.Version.ToString());
        Assert.Null(ModPackMemberReleases.NewerRelease(snapshot, "A", ModVersion.Parse("1.1.0")));
    }

    [Fact]
    public void NewerRelease_PinTheSnapshotDoesNotHave_IsHeldToStable_AndAnUnlistedModHasNone()
    {
        var snapshot = Snapshot(Release("A", "1.1.0-beta.1", ReleaseStatus.Testing), Release("A", "1.0.2"));

        Assert.Equal("1.0.2", ModPackMemberReleases.NewerRelease(snapshot, "A", ModVersion.Parse("1.0.1-beta.1"))!.Version.ToString());
        Assert.Null(ModPackMemberReleases.NewerRelease(snapshot, "Unlisted", ModVersion.Parse("1.0.0")));
    }

    private static ContentIndexSnapshot Snapshot(params ModVersionMetadata[] releases) =>
        new(1, releases.GroupBy(release => release.ModId).Select(group => new ContentIndexListing(group.Key, null, group.ToList(), null)).ToList(), [], null, []);
}
