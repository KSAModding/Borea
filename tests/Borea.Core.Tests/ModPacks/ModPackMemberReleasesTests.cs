using Borea.Core.ModPacks;
using Borea.Core.Mods;
using static Borea.Core.Tests.ModPacks.PackMemberRepository;

namespace Borea.Core.Tests.ModPacks;

public sealed class ModPackMemberReleasesTests
{
    [Fact]
    public async Task FindAsync_MembersAtTheirNewestRelease_FindsNothing()
    {
        var mods = new PackMemberRepository([], [Release("A", "1.0.0"), Release("B", "2.0.0"), Release("B", "1.0.0")]);

        var newer = await ModPackMemberReleases.FindAsync(Pack(("A", "1.0.0"), ("B", "2.0.0")), mods, ReleaseChannel.Stable);

        Assert.Empty(newer);
    }

    [Fact]
    public async Task FindAsync_NamesTheNewerReleaseOfEachMember_InPackOrder_AndLeavesOutAnUnlistedPin()
    {
        var mods = new PackMemberRepository([], [Release("A", "1.1.0"), Release("A", "1.0.0"), Release("B", "3.0.0"), Release("B", "2.0.0"), Release("C", "1.0.0")]);

        var newer = await ModPackMemberReleases.FindAsync(Pack(("B", "2.0.0"), ("Unlisted", "1.0.0"), ("C", "1.0.0"), ("A", "1.0.0")), mods, ReleaseChannel.Stable);

        Assert.Equal([("B", "2.0.0", "3.0.0"), ("A", "1.0.0", "1.1.0")], newer.Select(member => (member.ModId, member.Pinned.ToString(), member.Newer.Version.ToString())));
    }

    [Fact]
    public async Task FindAsync_StablePin_IsOutdatedOnlyByANewerStableRelease_InTheStableChannel()
    {
        var mods = new PackMemberRepository([], [Release("A", "1.2.0-dev.1", ReleaseStatus.Dev), Release("A", "1.1.0", yanked: true), Release("A", "1.1.0-beta.1", ReleaseStatus.Testing), Release("A", "1.0.0")]);
        var pack = Pack(("A", "1.0.0"));

        Assert.Empty(await ModPackMemberReleases.FindAsync(pack, mods, ReleaseChannel.Stable));
        Assert.Equal("1.1.0-beta.1", Assert.Single(await ModPackMemberReleases.FindAsync(pack, mods, ReleaseChannel.Testing)).Newer.Version.ToString());
        Assert.Equal("1.2.0-dev.1", Assert.Single(await ModPackMemberReleases.FindAsync(pack, mods, ReleaseChannel.Dev)).Newer.Version.ToString());
    }

    [Fact]
    public async Task FindAsync_TestingPin_IsComparedWithTheTestingChannel_EvenInTheStableChannel()
    {
        var mods = new PackMemberRepository([], [Release("A", "1.2.0-dev.1", ReleaseStatus.Dev), Release("A", "1.1.0-beta.2", ReleaseStatus.Testing), Release("A", "1.1.0-beta.1", ReleaseStatus.Testing)]);

        var newer = await ModPackMemberReleases.FindAsync(Pack(("A", "1.1.0-beta.1")), mods, ReleaseChannel.Stable);

        Assert.Equal("1.1.0-beta.2", Assert.Single(newer).Newer.Version.ToString());
    }
}
