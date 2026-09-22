using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Launch;

public sealed class LoaderNeedTests
{
    [Fact]
    public void For_NoModNamesALoader_IsNone()
    {
        var need = LoaderNeed.For(InstanceWith(TestFixtures.SampleInstalledMod("parts-pack")));

        Assert.True(need.IsNone);
        Assert.False(need.NeedsDifferentLoaders);
        Assert.Empty(need.Loaders);
    }

    [Fact]
    public void For_SeveralModsNeedOneLoader_TakesTheRangeEveryModAccepts()
    {
        var need = LoaderNeed.For(InstanceWith(
            NeedsLoader("orbit-tools", "StarMap", "0.4.0"),
            NeedsLoader("flight-tools", "starmap", "0.4.5", max: "0.9.0"),
            NeedsLoader("dock-tools", "StarMap", "0.3.0", max: "0.8.0"),
            TestFixtures.SampleInstalledMod("parts-pack")));

        var loader = Assert.Single(need.Loaders);
        Assert.Equal("0.4.5", loader.MinVersion.ToString());
        Assert.Equal("0.8.0", loader.MaxVersion?.ToString());
        Assert.Equal(["dock-tools", "flight-tools", "orbit-tools"], loader.NeededBy);
        Assert.False(loader.HasConflict);
        Assert.False(need.NeedsDifferentLoaders);
    }

    [Fact]
    public void For_NoModSetsAMaximum_LeavesTheRangeOpen()
    {
        var loader = Assert.Single(LoaderNeed.For(InstanceWith(NeedsLoader("orbit-tools", "StarMap", "0.4.0"))).Loaders);

        Assert.Null(loader.MaxVersion);
        Assert.True(loader.Accepts(ModVersion.Parse("9.0.0")));
    }

    [Fact]
    public void For_RangesThatDoNotOverlap_AreAConflict()
    {
        var loader = Assert.Single(LoaderNeed.For(InstanceWith(
            NeedsLoader("old-mod", "StarMap", "0.3.0", max: "0.3.9"),
            NeedsLoader("new-mod", "StarMap", "0.4.5"))).Loaders);

        Assert.True(loader.HasConflict);
        Assert.False(loader.Accepts(ModVersion.Parse("0.4.5")));
        Assert.False(loader.Accepts(ModVersion.Parse("0.3.5")));
    }

    [Fact]
    public void For_ModsNameDifferentLoaders_ListsEachInIdOrder()
    {
        var need = LoaderNeed.For(InstanceWith(
            NeedsLoader("orbit-tools", "StarMap", "0.4.0"),
            NeedsLoader("other-tools", "OtherLoader", "1.0.0")));

        Assert.True(need.NeedsDifferentLoaders);
        Assert.Equal(["OtherLoader", "StarMap"], need.Loaders.Select(loader => loader.LoaderId));
    }

    [Theory]
    [InlineData("0.4.4", false)]
    [InlineData("0.4.5", true)]
    [InlineData("0.8.0", true)]
    [InlineData("0.8.1", false)]
    public void Accepts_IsInclusiveAtBothEnds(string version, bool accepted)
    {
        var loader = Assert.Single(LoaderNeed.For(InstanceWith(NeedsLoader("orbit-tools", "StarMap", "0.4.5", max: "0.8.0"))).Loaders);

        Assert.Equal(accepted, loader.Accepts(ModVersion.Parse(version)));
    }

    private static Instance InstanceWith(params InstalledMod[] mods) =>
        Instance.FromExisting(Guid.NewGuid(), "Main", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, [], isFavorite: false);

    private static InstalledMod NeedsLoader(string modId, string loaderId, string min, string? max = null)
    {
        var release = new ModVersionMetadata(
            specVersion: 1,
            modId: modId,
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: TestFixtures.SampleDownload(),
            installSizeBytes: 2048,
            dependencies: [],
            loader: new LoaderRequirement(loaderId, ModVersion.Parse(min), max is null ? null : ModVersion.Parse(max)));
        return new InstalledMod(modId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release);
    }
}
