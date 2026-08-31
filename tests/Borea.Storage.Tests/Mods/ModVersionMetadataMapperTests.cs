using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Storage.Mods;
using Borea.Storage.Toml;

namespace Borea.Storage.Tests.Mods;

public sealed class ModVersionMetadataMapperTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private async Task<(ModVersionMetadata Reloaded, ModVersionMetadataDto ReloadedDto, string TomlText)> RoundTripAsync(ModVersionMetadata original)
    {
        var path = Path.Combine(_tempRoot, "release.toml");
        await TomlFileStore.WriteAsync(path, ModVersionMetadataMapper.ToDto(original));
        var reloadedDto = await TomlFileStore.ReadAsync<ModVersionMetadataDto>(path);
        return (ModVersionMetadataMapper.FromDto(reloadedDto!), reloadedDto!, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RoundTrip_FullShape_PreservesEveryField()
    {
        var original = MetadataFixtures.FullRelease();

        var (reloaded, _, tomlText) = await RoundTripAsync(original);

        Assert.Equal(original.SpecVersion, reloaded.SpecVersion);
        Assert.Equal(original.ModId, reloaded.ModId);
        Assert.Equal(original.Type, reloaded.Type);
        Assert.Equal(original.Version, reloaded.Version);
        Assert.Equal(original.VersionScheme, reloaded.VersionScheme);
        Assert.Equal(ReleaseStatus.Testing, reloaded.ReleaseStatus);
        Assert.Equal(original.ReleaseDate, reloaded.ReleaseDate);
        Assert.Equal(original.GameMin, reloaded.GameMin);
        Assert.Equal(original.GameMinRevision, reloaded.GameMinRevision);
        Assert.Equal(original.GameMax, reloaded.GameMax);
        Assert.Equal(original.GameMaxRevision, reloaded.GameMaxRevision);
        Assert.Equal(original.Os, reloaded.Os);
        Assert.Equal(original.InstallSizeBytes, reloaded.InstallSizeBytes);
        Assert.Equal(original.Changelog, reloaded.Changelog);
        Assert.True(reloaded.Yanked);
        Assert.Equal(original.YankedReason, reloaded.YankedReason);
        Assert.Equal("TestSource", reloaded.Source);

        Assert.Equal(original.Download.Url, reloaded.Download.Url);
        Assert.Equal(original.Download.Sha256, reloaded.Download.Sha256);
        Assert.Equal(original.Download.SizeBytes, reloaded.Download.SizeBytes);
        Assert.Equal(original.Download.ContentType, reloaded.Download.ContentType);
        Assert.Equal(original.Download.Mirrors, reloaded.Download.Mirrors);

        Assert.NotNull(reloaded.Install);
        Assert.Equal(original.Install!.Root, reloaded.Install!.Root);
        Assert.True(reloaded.Install.Derived);

        Assert.NotNull(reloaded.Loader);
        Assert.Equal(MetadataSource.Authored, reloaded.Loader!.Source);

        Assert.Equal(2, reloaded.Dependencies.Count);
        Assert.Equal(MetadataSource.Authored, reloaded.Dependencies[0].Source);
        Assert.Equal(MetadataSource.Derived, reloaded.Dependencies[1].Source);

        Assert.NotNull(reloaded.Listing);
        Assert.Equal(original.Listing!.Name, reloaded.Listing!.Name);
        Assert.Equal(original.Listing.Description, reloaded.Listing.Description);
        Assert.Equal(original.Listing.Links["forums"], reloaded.Listing.Links["forums"]);

        // The optional keys are really in the file when set, proving the
        // absence assertions in the minimal test can fail.
        Assert.Contains("GameMax", tomlText);
        Assert.Contains("Os = ", tomlText);
        Assert.Contains("Changelog", tomlText);
        Assert.Contains("Listing", tomlText);
        Assert.Contains("YankedReason", tomlText);

        // The enum vocabulary on disk stays the format's lowercase spelling.
        Assert.Contains("ReleaseStatus = \"testing\"", tomlText);
        Assert.Contains("Kind = \"required\"", tomlText);
        Assert.Contains("Source = \"derived\"", tomlText);
    }

    [Fact]
    public async Task RoundTrip_MinimalShape_AbsentOptionalsStayAbsent()
    {
        var original = MetadataFixtures.MinimalRelease();

        var (reloaded, reloadedDto, tomlText) = await RoundTripAsync(original);

        Assert.Null(reloadedDto.GameMax);
        Assert.Null(reloadedDto.GameMaxRevision);
        Assert.Null(reloadedDto.Os);
        Assert.Null(reloadedDto.Install);
        Assert.Null(reloadedDto.Loader);
        Assert.Null(reloadedDto.Changelog);
        Assert.Null(reloadedDto.Listing);
        Assert.Null(reloadedDto.YankedReason);

        Assert.Null(reloaded.GameMax);
        Assert.Null(reloaded.GameMaxRevision);
        Assert.Null(reloaded.Os);
        Assert.Null(reloaded.Install);
        Assert.Null(reloaded.Loader);
        Assert.False(reloaded.Yanked);
        Assert.Null(reloaded.Source);
        Assert.Empty(reloaded.Dependencies);
        Assert.Empty(reloaded.Download.Mirrors);

        Assert.DoesNotContain("GameMax", tomlText);
        Assert.DoesNotContain("Os = ", tomlText);
        Assert.DoesNotContain("Changelog", tomlText);
        Assert.DoesNotContain("Listing", tomlText);
        Assert.DoesNotContain("YankedReason", tomlText);
    }

    [Fact]
    public async Task RoundTrip_TimestampKeepsSubSecondPrecision()
    {
        var original = MetadataFixtures.MinimalRelease();

        var (reloaded, _, _) = await RoundTripAsync(original);

        Assert.Equal(MetadataFixtures.SampleTimestamp(), reloaded.ReleaseDate);
        Assert.Equal(MetadataFixtures.SampleTimestamp().Ticks, reloaded.ReleaseDate.Ticks);
    }

    [Fact]
    public async Task RoundTrip_LowercaseDigest_ComesBackNormalized()
    {
        var original = MetadataFixtures.MinimalRelease();

        var (reloaded, _, _) = await RoundTripAsync(original);

        Assert.Equal(new string('B', 64), reloaded.Download.Sha256);
        Assert.True(reloaded.Download.HashMatches(new string('b', 64)));
    }

    [Fact]
    public async Task RoundTrip_ModLoaderRelease_KeepsItsType()
    {
        var original = new ModVersionMetadata(
            specVersion: 1,
            modId: "StarMap",
            version: ModVersion.Parse("0.4.6"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: MetadataFixtures.SampleTimestamp(),
            gameMin: "2026.7",
            gameMinRevision: 2131,
            download: MetadataFixtures.SampleDownload(),
            installSizeBytes: 1024,
            dependencies: Array.Empty<ModDependency>(),
            type: ContentType.ModLoader);

        var (reloaded, _, tomlText) = await RoundTripAsync(original);

        Assert.Equal(ContentType.ModLoader, reloaded.Type);
        Assert.Null(reloaded.Install);
        Assert.Contains("Type = \"mod-loader\"", tomlText);
    }

    [Fact]
    public async Task RoundTrip_ModInstallShape_KeepsTheDerivedRootAndNoTarget()
    {
        // releases/AdvancedFlightComputer/0.7.2.json, as the index stamps it.
        var original = new ModVersionMetadata(
            specVersion: 1,
            modId: "AdvancedFlightComputer",
            version: ModVersion.Parse("0.7.2"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: MetadataFixtures.SampleTimestamp(),
            gameMin: "2026.8.3.5117",
            gameMinRevision: 5117,
            download: MetadataFixtures.SampleDownload(),
            installSizeBytes: 326889,
            dependencies: Array.Empty<ModDependency>(),
            install: new InstallInfo("AdvancedFlightComputer", derived: true));

        var (reloaded, _, tomlText) = await RoundTripAsync(original);

        Assert.Equal("AdvancedFlightComputer", reloaded.Install!.Root);
        Assert.True(reloaded.Install.Derived);
        Assert.Null(reloaded.Install.Target);
        Assert.Null(reloaded.Install.Path);
        Assert.DoesNotContain("Target", tomlText);
    }

    [Fact]
    public async Task RoundTrip_LoaderInstallShape_KeepsTheStandaloneTargetWithoutARoot()
    {
        // releases/StarMap/0.4.6.json, as the index stamps it.
        var original = new ModVersionMetadata(
            specVersion: 1,
            modId: "StarMap",
            version: ModVersion.Parse("0.4.6"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: MetadataFixtures.SampleTimestamp(),
            gameMin: "2026.8.3.5117",
            gameMinRevision: 5117,
            download: MetadataFixtures.SampleDownload(),
            installSizeBytes: 2549644,
            dependencies: Array.Empty<ModDependency>(),
            type: ContentType.ModLoader,
            install: new InstallInfo(null, derived: true, InstallAnchor.Standalone));

        var (reloaded, reloadedDto, tomlText) = await RoundTripAsync(original);

        Assert.Equal(ContentType.ModLoader, reloaded.Type);
        Assert.Null(reloaded.Install!.Root);
        Assert.True(reloaded.Install.Derived);
        Assert.Equal(InstallAnchor.Standalone, reloaded.Install.Target);
        Assert.Null(reloadedDto.Install!.Root);
        Assert.Contains("Target = \"standalone\"", tomlText);
        Assert.DoesNotContain("Root", tomlText);
    }

    [Fact]
    public void FromDto_AnchorThisBuildDoesNotKnow_ParsesToUnknown()
    {
        var dto = ModVersionMetadataMapper.ToDto(MetadataFixtures.FullRelease());
        dto.Install!.Target = "cache-dir";

        var reloaded = ModVersionMetadataMapper.FromDto(dto);

        Assert.Equal(InstallAnchor.Unknown, reloaded.Install!.Target);
    }

    [Fact]
    public async Task RoundTrip_UnknownChecksumAndSizes_StayAbsent()
    {
        // Facts a source cannot provide persist as absent, never as zero.
        var original = new ModVersionMetadata(
            specVersion: 1,
            modId: "test-mod",
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: MetadataFixtures.SampleTimestamp(),
            gameMin: "2026.7.4.2131",
            gameMinRevision: 2131,
            download: new DownloadInfo("https://example.com/mod.zip", sha256: null, sizeBytes: null, "application/zip"),
            installSizeBytes: null,
            dependencies: Array.Empty<ModDependency>());

        var (reloaded, reloadedDto, tomlText) = await RoundTripAsync(original);

        Assert.Null(reloaded.Download.Sha256);
        Assert.Null(reloaded.Download.SizeBytes);
        Assert.Null(reloaded.InstallSizeBytes);
        Assert.False(reloaded.Download.HashMatches(new string('A', 64)));
        Assert.Null(reloadedDto.Download.Sha256);
        Assert.DoesNotContain("Sha256", tomlText);
        Assert.DoesNotContain("SizeBytes", tomlText);
    }

    [Fact]
    public void FromDto_AbsentVersionScheme_DefaultsToSemver()
    {
        var dto = ModVersionMetadataMapper.ToDto(MetadataFixtures.MinimalRelease());
        dto.VersionScheme = "semver";

        Assert.Equal("semver", ModVersionMetadataMapper.FromDto(dto).VersionScheme);
        Assert.Equal("semver", new ModVersionMetadataDto().VersionScheme);
    }

    [Fact]
    public void FromDto_UnknownReleaseStatus_ParsesToUnknown()
    {
        var dto = ModVersionMetadataMapper.ToDto(MetadataFixtures.MinimalRelease());
        dto.ReleaseStatus = "nightly";

        Assert.Equal(ReleaseStatus.Unknown, ModVersionMetadataMapper.FromDto(dto).ReleaseStatus);
    }

    [Fact]
    public void FromDto_MissingSpecVersion_NamesThePreModelShape()
    {
        var dto = ModVersionMetadataMapper.ToDto(MetadataFixtures.MinimalRelease());
        dto.SpecVersion = 0;

        var exception = Assert.Throws<FormatException>(() => ModVersionMetadataMapper.FromDto(dto));
        Assert.Contains("spec version", exception.Message);
    }

    [Fact]
    public void FromDto_NegativeSpecVersion_IsAMalformedFile()
    {
        var dto = ModVersionMetadataMapper.ToDto(MetadataFixtures.MinimalRelease());
        dto.SpecVersion = -3;

        var exception = Assert.Throws<FormatException>(() => ModVersionMetadataMapper.FromDto(dto));
        Assert.Contains("spec version", exception.Message);
    }

    [Fact]
    public void FromDto_SpecVersionAboveHighest_StillMaps()
    {
        var dto = ModVersionMetadataMapper.ToDto(MetadataFixtures.MinimalRelease());
        dto.SpecVersion = SpecVersions.Highest + 1;

        var mapped = ModVersionMetadataMapper.FromDto(dto);

        Assert.Equal(SpecVersions.Highest + 1, mapped.SpecVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
