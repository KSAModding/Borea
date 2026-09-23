using Borea.Core.ModPacks;
using static Borea.Core.Tests.ModPacks.PackMemberRepository;

namespace Borea.Core.Tests.ModPacks;

public sealed class ModPackForumListTests
{
    [Fact]
    public async Task WriteAsync_OneLinePerMemberWithTheSixFacts_InPackOrder()
    {
        var mods = new PackMemberRepository(
            [Listing("AdvancedFlightComputer", "Advanced Flight Computer"), Listing("StarMap", "StarMap")],
            [Release("AdvancedFlightComputer", "0.8.0"), Release("AdvancedFlightComputer", "0.7.5"), Release("StarMap", "0.4.7")]);

        var lines = await ModPackForumList.WriteAsync(Pack(("StarMap", "0.4.7"), ("Unlisted", "1.0.0"), ("AdvancedFlightComputer", "0.7.5")), mods);

        Assert.Equal(
        [
            "StarMap 0.4.7 - Author: Maxi, cairn5 - License: MIT - Download: https://example.com/StarMap/0.4.7.zip - Thread: https://forums.example/StarMap",
            "Unlisted 1.0.0 - Not listed in the content index",
            "Advanced Flight Computer 0.7.5 - Author: Maxi, cairn5 - License: MIT - Download: https://example.com/AdvancedFlightComputer/0.7.5.zip - Thread: https://forums.example/AdvancedFlightComputer",
        ], lines);
    }

    [Fact]
    public async Task WriteAsync_PinnedVersionTheIndexDoesNotList_IsNamedAsUnlisted()
    {
        var mods = new PackMemberRepository([Listing("StarMap", "StarMap")], [Release("StarMap", "0.4.7")]);

        var lines = await ModPackForumList.WriteAsync(Pack(("StarMap", "0.4.6")), mods);

        Assert.Equal(["StarMap 0.4.6 - Not listed in the content index"], lines);
    }
}
