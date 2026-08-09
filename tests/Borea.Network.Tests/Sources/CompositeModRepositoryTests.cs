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
    public async Task Tag_PreservesEveryListingFact()
    {
        // The fixture populates every optional field, so dropping any
        // argument from the Tag copy makes an assertion below fail.
        var original = TestFixtures.FullModMetadata();
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = new FakeModRepository(original),
        });

        var tagged = (await composite.GetAvailableModsAsync())[0];

        Assert.Equal("spacedock", tagged.Source);
        Assert.Equal(original.SpecVersion, tagged.SpecVersion);
        Assert.Equal(original.ModId, tagged.ModId);
        Assert.Equal(original.Type, tagged.Type);
        Assert.Equal(original.Name, tagged.Name);
        Assert.Equal(original.Authors, tagged.Authors);
        Assert.Equal(original.Abstract, tagged.Abstract);
        Assert.Equal(original.Description, tagged.Description);
        Assert.Equal(original.License, tagged.License);
        Assert.Equal(original.Tags, tagged.Tags);
        Assert.Equal(original.Status, tagged.Status);
        Assert.Equal(original.SupersededBy, tagged.SupersededBy);
        Assert.Equal(original.Links.Count, tagged.Links.Count);
        Assert.Equal(original.ForumUrl, tagged.ForumUrl);
        Assert.Equal(original.Links["repository"], tagged.Links["repository"]);
        Assert.NotNull(tagged.Releases);
        Assert.Equal(original.Releases!.Authority, tagged.Releases!.Authority);
        Assert.Equal(original.Releases.Hosts.Count, tagged.Releases.Hosts.Count);
        Assert.Equal(original.GameMin, tagged.GameMin);
        Assert.Equal(original.GameMax, tagged.GameMax);
        Assert.Equal(original.Os, tagged.Os);
        Assert.NotNull(tagged.Loader);
        Assert.Equal(original.Loader!.LoaderId, tagged.Loader!.LoaderId);
        Assert.Equal(original.Loader.MinVersion, tagged.Loader.MinVersion);
        Assert.Equal(original.Loader.MaxVersion, tagged.Loader.MaxVersion);
        Assert.Equal(original.Dependencies.Count, tagged.Dependencies.Count);
        Assert.Equal(original.Dependencies[0].ModId, tagged.Dependencies[0].ModId);
        Assert.True(tagged.Dependencies[1].IsAnyOf);
        Assert.Equal(original.InstallRootOverride, tagged.InstallRootOverride);
    }

    [Fact]
    public async Task Tag_PreservesEveryReleaseFact()
    {
        // FullRelease is yanked, so GetReleaseAsync is the accessor under
        // test here, which returns yanked releases as-is by contract.
        var original = TestFixtures.FullRelease();
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = new FakeModRepository(Array.Empty<ModMetadata>(), new[] { original }),
        });

        var tagged = await composite.GetReleaseAsync(original.ModId, original.Version);

        Assert.NotNull(tagged);
        Assert.Equal("spacedock", tagged!.Source);
        Assert.Equal(original.SpecVersion, tagged.SpecVersion);
        Assert.Equal(original.ModId, tagged.ModId);
        Assert.Equal(original.Type, tagged.Type);
        Assert.Equal(original.Version, tagged.Version);
        Assert.Equal(original.VersionScheme, tagged.VersionScheme);
        Assert.Equal(original.ReleaseStatus, tagged.ReleaseStatus);
        Assert.Equal(original.ReleaseDate, tagged.ReleaseDate);
        Assert.Equal(original.GameMin, tagged.GameMin);
        Assert.Equal(original.GameMinRevision, tagged.GameMinRevision);
        Assert.Equal(original.GameMax, tagged.GameMax);
        Assert.Equal(original.GameMaxRevision, tagged.GameMaxRevision);
        Assert.Equal(original.Os, tagged.Os);
        Assert.Equal(original.Download.Url, tagged.Download.Url);
        Assert.Equal(original.Download.Sha256, tagged.Download.Sha256);
        Assert.Equal(original.Download.SizeBytes, tagged.Download.SizeBytes);
        Assert.Equal(original.Download.ContentType, tagged.Download.ContentType);
        Assert.Equal(original.Download.Mirrors, tagged.Download.Mirrors);
        Assert.Equal(original.InstallSizeBytes, tagged.InstallSizeBytes);
        Assert.NotNull(tagged.Install);
        Assert.Equal(original.Install!.Root, tagged.Install!.Root);
        Assert.Equal(original.Install.Derived, tagged.Install.Derived);
        Assert.NotNull(tagged.Loader);
        Assert.Equal(original.Loader!.LoaderId, tagged.Loader!.LoaderId);
        Assert.Equal(original.Loader.Source, tagged.Loader.Source);
        Assert.Equal(original.Dependencies.Count, tagged.Dependencies.Count);
        Assert.Equal(original.Dependencies[0].Source, tagged.Dependencies[0].Source);
        Assert.Equal(original.Changelog, tagged.Changelog);
        Assert.NotNull(tagged.Listing);
        Assert.Equal(original.Listing!.Name, tagged.Listing!.Name);
        Assert.Equal(original.Listing.Description, tagged.Listing.Description);
        Assert.True(tagged.Yanked);
        Assert.Equal(original.YankedReason, tagged.YankedReason);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ReturnsFirstMatchingSource_TaggedCorrectly()
    {
        var spaceDock = new FakeModRepository(
            new[] { TestFixtures.SampleModMetadata("mod-a", "irrelevant") },
            new[] { TestFixtures.SampleRelease("mod-a") });
        var borea = new FakeModRepository();
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
            ["borea"] = borea,
        });

        var release = await composite.GetLatestReleaseAsync("mod-a");

        Assert.NotNull(release);
        Assert.Equal("spacedock", release!.Source);
        Assert.Equal(ModVersion.Parse("1.0.0"), release.Version);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_NoSourceHasIt_ReturnsNull()
    {
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = new FakeModRepository(),
        });

        var release = await composite.GetLatestReleaseAsync("never-exists");

        Assert.Null(release);
    }

    [Fact]
    public async Task GetReleaseAsync_MatchesOnBothModIdAndVersion()
    {
        var spaceDock = new FakeModRepository(
            Array.Empty<ModMetadata>(),
            new[] { TestFixtures.SampleRelease("mod-a", "1.0.0") });
        var composite = new CompositeModRepository(new Dictionary<string, IModRepository>
        {
            ["spacedock"] = spaceDock,
        });

        var wrongVersion = await composite.GetReleaseAsync("mod-a", ModVersion.Parse("2.0.0"));
        var rightVersion = await composite.GetReleaseAsync("mod-a", ModVersion.Parse("1.0.0"));

        Assert.Null(wrongVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal("spacedock", rightVersion!.Source);
    }

    [Fact]
    public async Task GetAvailableVersionsAsync_ReturnsFirstNonEmptySourceResult()
    {
        var spaceDock = new FakeModRepository(); // No releases for "mod-a".
        var borea = new FakeModRepository(
            Array.Empty<ModMetadata>(),
            new[] { TestFixtures.SampleRelease("mod-a") });
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
            ["borea"] = new FakeModRepository(
                new[] { TestFixtures.SampleModMetadata("mod-a", "borea") },
                new[] { TestFixtures.SampleRelease("mod-a") }),
            // "spacedock" deliberately absent.
        });

        var release = await composite.GetLatestReleaseAsync("mod-a");

        Assert.NotNull(release);
        Assert.Equal("borea", release!.Source);
    }
}
