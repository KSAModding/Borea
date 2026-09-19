using Borea.Core.Mods;
using Borea.Network.SpaceDock;

namespace Borea.Network.Tests.SpaceDock;

public sealed class OfflineSpaceDockModRepositoryTests
{
    [Fact]
    public async Task Lookups_ModIdThatSpaceDockResolves_Fail()
    {
        var repository = new OfflineSpaceDockModRepository(new SpaceDockResolver());

        await Assert.ThrowsAsync<NotSupportedException>(() => repository.GetAvailableVersionsAsync("4253"));
        await Assert.ThrowsAsync<NotSupportedException>(() => repository.GetReleaseAsync("4253", ModVersion.Parse("1.0.0")));
        await Assert.ThrowsAsync<NotSupportedException>(() => repository.GetAvailableModsAsync());
    }

    [Fact]
    public async Task Lookups_ModIdThatSpaceDockCannotResolve_FindNothing()
    {
        var repository = new OfflineSpaceDockModRepository(new SpaceDockResolver());

        Assert.Empty(await repository.GetAvailableVersionsAsync("KittenExtensions"));
        Assert.Null(await repository.GetReleaseAsync("KittenExtensions", ModVersion.Parse("1.0.0")));
        Assert.Null(await repository.GetLatestReleaseAsync("KittenExtensions"));
    }
}
