using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Game;

public sealed class CompatibilityTests
{
    private static GameVersion Installed(int revision) => new(2026, 8, 3, revision);

    private static ModVersionMetadata Release(
        string gameMin = "2026.7.4.2131",
        int gameMinRevision = 2131,
        string? gameMax = null,
        int? gameMaxRevision = null,
        IReadOnlyList<string>? os = null) =>
        new(
            specVersion: SpecVersions.Highest,
            modId: "test-mod",
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: gameMin,
            gameMinRevision: gameMinRevision,
            download: TestFixtures.SampleDownload(),
            installSizeBytes: 2048,
            dependencies: Array.Empty<ModDependency>(),
            gameMax: gameMax,
            gameMaxRevision: gameMaxRevision,
            os: os);

    private static ModPackMetadata Pack(string gameMin, string? gameMax = null) =>
        new(
            specVersion: SpecVersions.Highest,
            modPackId: "test-pack",
            source: "index",
            name: "Test pack",
            authors: ["Maxi"],
            abstractText: "A pack.",
            license: "MIT",
            links: new Dictionary<string, string> { ["forums"] = "https://example.com/test-pack" },
            gameMin: gameMin,
            version: ModVersion.Parse("1.0.0"),
            releasedAt: DateTimeOffset.UtcNow,
            mods: [new ModPackEntry("test-mod", ModVersion.Parse("1.0.0"))],
            gameMax: gameMax);

    [Fact]
    public void Evaluate_NoLowerBound_IsUnknown()
    {
        Assert.Equal(GameCompatibility.Unknown, Compatibility.Evaluate(null, null, Installed(5117)));
    }

    [Fact]
    public void Evaluate_NoLowerBoundButAnUpperOne_IsStillUnknown()
    {
        Assert.Equal(GameCompatibility.Unknown, Compatibility.Evaluate(null, 5117, Installed(2131)));
    }

    [Fact]
    public void Evaluate_UnknownInstalledVersion_IsUnknown()
    {
        Assert.Equal(GameCompatibility.Unknown, Compatibility.Evaluate(2131, null, null));
        Assert.Equal(GameCompatibility.Unknown, Compatibility.Evaluate(Release(), null));
    }

    [Fact]
    public void Evaluate_BelowTheLowerBound_IsIncompatible()
    {
        Assert.Equal(GameCompatibility.Incompatible, Compatibility.Evaluate(5117, null, Installed(5116)));
    }

    [Fact]
    public void Evaluate_AtTheLowerBound_IsCompatible()
    {
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(5117, null, Installed(5117)));
    }

    [Fact]
    public void Evaluate_AboveAnOpenLowerBound_IsCompatible()
    {
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(2131, null, Installed(5348)));
    }

    [Fact]
    public void Evaluate_AtTheUpperBound_IsCompatible()
    {
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(2131, 5117, Installed(5117)));
    }

    [Fact]
    public void Evaluate_AboveTheUpperBound_IsUntested()
    {
        Assert.Equal(GameCompatibility.Untested, Compatibility.Evaluate(2131, 5117, Installed(5118)));
    }

    [Fact]
    public void Evaluate_BoundsNamingOneRevision_AcceptOnlyThatRevision()
    {
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(5117, 5117, Installed(5117)));
        Assert.Equal(GameCompatibility.Incompatible, Compatibility.Evaluate(5117, 5117, Installed(5116)));
        Assert.Equal(GameCompatibility.Untested, Compatibility.Evaluate(5117, 5117, Installed(5118)));
    }

    [Fact]
    public void Evaluate_IgnoresEveryComponentButTheRevision()
    {
        var installed = new GameVersion(2030, 12, 99, 2130);

        Assert.Equal(GameCompatibility.Incompatible, Compatibility.Evaluate(2131, null, installed));
    }

    [Fact]
    public void Evaluate_Release_ReadsItsStampedBounds()
    {
        var release = Release(gameMax: "2026.8.3.5117", gameMaxRevision: 5117);

        Assert.Equal(GameCompatibility.Incompatible, Compatibility.Evaluate(release, Installed(2130)));
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(release, Installed(2131)));
        Assert.Equal(GameCompatibility.Untested, Compatibility.Evaluate(release, Installed(5118)));
    }

    [Fact]
    public void Evaluate_Release_WithAKnownInstalledVersion_NeverAnswersUnknown()
    {
        // A release always carries a lower bound.
        Assert.NotEqual(GameCompatibility.Unknown, Compatibility.Evaluate(Release(), Installed(1)));
        Assert.NotEqual(GameCompatibility.Unknown, Compatibility.Evaluate(Release(), Installed(int.MaxValue)));
    }

    [Fact]
    public void Evaluate_NullRelease_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Compatibility.Evaluate((ModVersionMetadata)null!, Installed(5117)));
    }

    [Fact]
    public void Evaluate_Pack_ReadsItsAuthoredBounds()
    {
        var pack = Pack("2026.7.4.2131", "2026.8.3.5117");

        Assert.Equal(GameCompatibility.Incompatible, Compatibility.Evaluate(pack, Installed(2130), GameReleaseList.Empty));
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(pack, Installed(2131), GameReleaseList.Empty));
        Assert.Equal(GameCompatibility.Untested, Compatibility.Evaluate(pack, Installed(5118), GameReleaseList.Empty));
        Assert.Equal(GameCompatibility.Unknown, Compatibility.Evaluate(pack, null, GameReleaseList.Empty));
    }

    [Fact]
    public void Evaluate_PackWithMonthBounds_AcceptsTheWholeMonth()
    {
        var releases = new GameReleaseList(["2026.7.2.4824", "2026.7.10.5056", "2026.8.3.5117"]);
        var pack = Pack("2026.7", "2026.7");

        Assert.Equal(GameCompatibility.Incompatible, Compatibility.Evaluate(pack, Installed(4823), releases));
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(pack, Installed(4824), releases));
        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(pack, Installed(5056), releases));
        Assert.Equal(GameCompatibility.Untested, Compatibility.Evaluate(pack, Installed(5057), releases));
    }

    [Fact]
    public void Evaluate_PackWithAnUpperBoundInTheNewestMonth_IsOpen()
    {
        var releases = new GameReleaseList(["2026.7.2.4824", "2026.8.3.5117"]);

        Assert.Equal(GameCompatibility.Compatible, Compatibility.Evaluate(Pack("2026.7", "2026.8"), Installed(9999), releases));
    }

    [Fact]
    public void Evaluate_InstalledGameOfALaterMonthThanTheList_ClosesTheUpperMonthBound()
    {
        var releases = new GameReleaseList(["2026.8.3.5117"]);

        Assert.Equal(GameCompatibility.Untested, Compatibility.Evaluate(Pack("2026.8", "2026.8"), new GameVersion(2026, 9, 7, 5402), releases));
    }

    [Fact]
    public void Evaluate_PackWithAMonthTheListDoesNotKnow_IsUnknownUnlessBelowTheLowerBound()
    {
        var releases = new GameReleaseList(["2026.8.3.5117"]);

        Assert.Equal(GameCompatibility.Unknown, Compatibility.Evaluate(Pack("2026.7"), Installed(5117), releases));
        Assert.Equal(GameCompatibility.Incompatible, Compatibility.Evaluate(Pack("2026.8.3.5117", "2026.9"), Installed(5116), releases));
        Assert.Equal(GameCompatibility.Unknown, Compatibility.Evaluate(Pack("2026.8.3.5117", "2026.9"), Installed(5117), releases));
    }

    [Theory]
    [InlineData(5261, null, 5117, 5261, true)]
    [InlineData(5262, null, 5117, 5261, false)]
    [InlineData(5117, 5261, 5261, 5348, true)]
    [InlineData(5117, 5260, 5261, 5348, false)]
    [InlineData(5261, 5261, 5261, 5261, true)]
    public void SupportsAnyBuild_RangeEndsIncludeTheBounds(int minRevision, int? maxRevision, int? fromRevision, int? toRevision, bool expected)
    {
        Assert.Equal(expected, Compatibility.SupportsAnyBuild(minRevision, maxRevision, fromRevision, toRevision));
    }

    [Theory]
    [InlineData(5117, 5402, null)]
    [InlineData(5117, 9000, 9000)]
    [InlineData(5402, null, null)]
    public void SupportsAnyBuild_OpenUpperBound_SupportsEveryLaterBuild(int minRevision, int? fromRevision, int? toRevision)
    {
        Assert.True(Compatibility.SupportsAnyBuild(minRevision, null, fromRevision, toRevision));
    }

    [Fact]
    public void SupportsAnyBuild_PackWithFullBounds_ComparesTheRevisions()
    {
        Assert.True(Compatibility.SupportsAnyBuild(Pack("2026.7.4.2131", "2026.8.3.5117"), 5117, null, GameReleaseList.Empty));
        Assert.False(Compatibility.SupportsAnyBuild(Pack("2026.7.4.2131", "2026.8.3.5117"), 5118, null, GameReleaseList.Empty));
    }

    [Fact]
    public void SupportsAnyBuild_PackWithMonthBounds_CoversTheWholeMonth()
    {
        var releases = new GameReleaseList(["2026.7.2.4824", "2026.7.10.5056", "2026.8.3.5117"]);
        var july = Pack("2026.7", "2026.7");

        Assert.False(Compatibility.SupportsAnyBuild(july, null, 4823, releases));
        Assert.True(Compatibility.SupportsAnyBuild(july, 4824, 4824, releases));
        Assert.True(Compatibility.SupportsAnyBuild(july, 5056, 5056, releases));
        Assert.False(Compatibility.SupportsAnyBuild(july, 5057, null, releases));
        Assert.True(Compatibility.SupportsAnyBuild(Pack("2026.7", "2026.8"), 9999, null, releases));
    }

    [Fact]
    public void SupportsAnyBuild_PackWithAMonthTheListDoesNotKnow_SupportsNoBuild()
    {
        var releases = new GameReleaseList(["2026.8.3.5117"]);

        Assert.False(Compatibility.SupportsAnyBuild(Pack("2026.7"), null, null, releases));
        Assert.False(Compatibility.SupportsAnyBuild(Pack("2026.8.3.5117", "2026.9"), 5117, 5117, releases));
    }

    [Fact]
    public void EvaluateOs_AbsentList_SupportsEveryPlatform()
    {
        var support = Compatibility.EvaluateOs(null, OsPlatform.Linux);

        Assert.True(support.IsSupported);
        Assert.Empty(support.Unrecognized);
    }

    [Fact]
    public void EvaluateOs_EmptyList_ReadsLikeAnAbsentOne()
    {
        var support = Compatibility.EvaluateOs(Array.Empty<string>(), OsPlatform.Linux);

        Assert.True(support.IsSupported);
        Assert.Empty(support.Unrecognized);
    }

    [Theory]
    [InlineData("windows", OsPlatform.Windows)]
    [InlineData("linux", OsPlatform.Linux)]
    [InlineData("macos", OsPlatform.MacOs)]
    public void EvaluateOs_ListNamingTheTarget_IsSupported(string entry, OsPlatform target)
    {
        Assert.True(Compatibility.EvaluateOs(new[] { entry }, target).IsSupported);
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("WINDOWS")]
    [InlineData("WiNdOwS")]
    public void EvaluateOs_ComparesCaseInsensitively(string entry)
    {
        Assert.True(Compatibility.EvaluateOs(new[] { entry }, OsPlatform.Windows).IsSupported);
    }

    [Fact]
    public void EvaluateOs_ListWithoutTheTarget_IsNotSupported()
    {
        var support = Compatibility.EvaluateOs(new[] { "windows", "macos" }, OsPlatform.Linux);

        Assert.False(support.IsSupported);
        Assert.Empty(support.Unrecognized);
    }

    [Fact]
    public void EvaluateOs_UnrecognizedEntry_DoesNotStopTheRecognizedOnes()
    {
        var support = Compatibility.EvaluateOs(new[] { "freebsd", "linux" }, OsPlatform.Linux);

        Assert.True(support.IsSupported);
        Assert.Equal(new[] { "freebsd" }, support.Unrecognized);
    }

    [Fact]
    public void EvaluateOs_OnlyUnrecognizedEntries_IsNotSupportedAndReportsThem()
    {
        var support = Compatibility.EvaluateOs(new[] { "FreeBSD", "haiku" }, OsPlatform.Windows);

        Assert.False(support.IsSupported);
        Assert.Equal(new[] { "FreeBSD", "haiku" }, support.Unrecognized);
    }

    [Fact]
    public void EvaluateOs_TheReleaseList_IsReadTheSameWay()
    {
        var release = Release(os: new[] { "windows" });

        Assert.True(Compatibility.EvaluateOs(release.Os, OsPlatform.Windows).IsSupported);
        Assert.False(Compatibility.EvaluateOs(release.Os, OsPlatform.MacOs).IsSupported);
    }
}
