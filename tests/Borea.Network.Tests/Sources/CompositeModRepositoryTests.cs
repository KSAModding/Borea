using Borea.Core.Mods;
using Borea.Network.Sources;
using Borea.Network.Tests.Temp;

namespace Borea.Network.Tests;

public sealed class CompositeModRepositoryTests
{
    [Fact]
    public void Constructor_NullSources_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeModRepository(null!));
    }

    [Fact]
    public async Task GetAvailableModsAsync_MergesAcrossSources()
    {
        var spaceDock = new FakeModRepository(TestFixtures.SampleModMetadata("mod-a", "spacedock"));
        var borea = new FakeModRepository(TestFixtures.SampleModMetadata("mod-b", "borea"));
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
            ["borea"] = borea,
        });

        var results = await composite.GetAvailableModsAsync();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetAvailableModsAsync_TagsEachResultWithItsOwnSource()
    {
        var spaceDock = new FakeModRepository(TestFixtures.SampleModMetadata("mod-a", "original-source-ignored"));
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
        });

        var results = await composite.GetAvailableModsAsync();

        // Confirms Tag() overwrites Source with the registry key, not
        // whatever the underlying repository happened to set.
        Assert.Equal("spacedock", results[0].Source);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsFirstMatchingSource_TaggedCorrectly()
    {
        var spaceDock = new FakeModRepository(TestFixtures.SampleModMetadata("mod-a", "irrelevant"));
        var borea = new FakeModRepository();
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
            ["borea"] = borea,
        });

        var result = await composite.GetLatestAsync("mod-a");

        Assert.NotNull(result);
        Assert.Equal("spacedock", result!.Source);
    }

    [Fact]
    public async Task GetLatestAsync_NoSourceHasIt_ReturnsNull()
    {
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = new FakeModRepository(),
        });

        var result = await composite.GetLatestAsync("never-exists");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersionAsync_MatchesOnBothModIdAndVersion()
    {
        var spaceDock = new FakeModRepository(TestFixtures.SampleModMetadata("mod-a", "spacedock", version: "1.0.0"));
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
        });

        var wrongVersion = await composite.GetVersionAsync("mod-a", ModVersion.Parse("2.0.0"));
        var rightVersion = await composite.GetVersionAsync("mod-a", ModVersion.Parse("1.0.0"));

        Assert.Null(wrongVersion);
        Assert.NotNull(rightVersion);
    }

    [Fact]
    public async Task GetAvailableVersionsAsync_ReturnsFirstNonEmptySourceResult()
    {
        var spaceDock = new FakeModRepository(); // No versions for "mod-a".
        var borea = new FakeModRepository(TestFixtures.SampleModMetadata("mod-a", "borea"));
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
            ["borea"] = borea,
        });

        var versions = await composite.GetAvailableVersionsAsync("mod-a");

        Assert.Single(versions);
    }

    [Fact]
    public async Task SearchAsync_MergesAndTagsAcrossSources()
    {
        var spaceDock = new FakeModRepository(TestFixtures.SampleModMetadata("mod-a", "spacedock", name: "Flight Manager"));
        var borea = new FakeModRepository(TestFixtures.SampleModMetadata("mod-b", "borea", name: "Flight Assist"));
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
            ["borea"] = borea,
        });

        var results = await composite.SearchAsync("Flight");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task DroppingASource_MakesItsResultsUnreachable()
    {
        // Confirms the "retiring SpaceDock later" story from the design
        // discussion: a source not present in the registry is simply never
        // queried, no special retirement handling needed.
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["borea"] = new FakeModRepository(TestFixtures.SampleModMetadata("mod-a", "borea")),
            // "spacedock" deliberately absent.
        });

        var result = await composite.GetLatestAsync("mod-a");

        Assert.NotNull(result);
        Assert.Equal("borea", result!.Source);
    }
}