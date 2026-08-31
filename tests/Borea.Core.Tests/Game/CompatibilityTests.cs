using Borea.Core.Dependencies;
using Borea.Core.Game;
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
        int? gameMaxRevision = null) =>
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
            gameMaxRevision: gameMaxRevision);

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
        Assert.Throws<ArgumentNullException>(() => Compatibility.Evaluate(null!, Installed(5117)));
    }
}
