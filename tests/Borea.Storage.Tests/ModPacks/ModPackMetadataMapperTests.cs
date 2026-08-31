using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Storage.ModPacks;
using Borea.Storage.Toml;

namespace Borea.Storage.Tests.ModPacks;

public sealed class ModPackMetadataMapperTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private static DateTimeOffset SampleTimestamp() =>
        new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    private static ModPackEntry Entry(string contentId, string version = "1.0.0") =>
        new(contentId, ModVersion.Parse(version));

    private static ModPackMetadata MinimalPack() => new(
        specVersion: SpecVersions.Highest,
        modPackId: "NavigationStarterPack",
        source: "TestSource",
        name: "Navigation Starter Pack",
        authors: new[] { "Maxi" },
        abstractText: "Abstract.",
        license: "CC0-1.0",
        links: new Dictionary<string, string> { ["forums"] = "https://forums.example/thread/1" },
        gameMin: "2026.7",
        version: ModVersion.Parse("1.0.0"),
        releasedAt: SampleTimestamp(),
        mods: new[] { Entry("AdvancedFlightComputer", "0.7.0") });

    private static ModPackMetadata FullPack() => new(
        specVersion: SpecVersions.Highest,
        modPackId: "NavigationStarterPack",
        source: "TestSource",
        name: "Navigation Starter Pack",
        authors: new[] { "Maxi", "Author B" },
        abstractText: "Everything you need for maneuver planning, tested together.",
        license: "CC0-1.0",
        links: new Dictionary<string, string>
        {
            ["forums"] = "https://forums.example/thread/1",
            ["homepage"] = "https://example.com/pack",
        },
        gameMin: "2026.8.3.5117",
        version: ModVersion.Parse("1.2.0-beta.2"),
        releasedAt: SampleTimestamp(),
        mods: new[] { Entry("AdvancedFlightComputer", "0.7.0"), Entry("KittenExtensions", "0.4.0") },
        tags: new[] { "navigation" },
        description: "A longer description.",
        status: ModStatus.Deprecated,
        supersededBy: "NavigationStarterPackNG",
        gameMax: "2026.8.22.5348",
        os: new[] { "windows" },
        changelog: "https://example.com/pack/changelog",
        vehicles: new[] { Entry("ReusableBoosterDemo", "1.2.0") },
        saves: new[] { Entry("ApolloRecreation") });

    private async Task<(ModPackMetadata Reloaded, ModPackMetadataDto ReloadedDto, string TomlText)> RoundTripAsync(ModPackMetadata original)
    {
        var path = Path.Combine(_tempRoot, "pack.toml");
        await TomlFileStore.WriteAsync(path, ModPackMetadataMapper.ToDto(original));
        var reloadedDto = await TomlFileStore.ReadAsync<ModPackMetadataDto>(path);
        return (ModPackMetadataMapper.FromDto(reloadedDto!), reloadedDto!, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RoundTrip_FullShape_PreservesEveryField()
    {
        var original = FullPack();

        var (reloaded, _, tomlText) = await RoundTripAsync(original);

        Assert.Equal(original.SpecVersion, reloaded.SpecVersion);
        Assert.Equal(original.ModPackId, reloaded.ModPackId);
        Assert.Equal(ContentType.ModPack, reloaded.Type);
        Assert.Equal(original.Source, reloaded.Source);
        Assert.Equal(original.Name, reloaded.Name);
        Assert.Equal(original.Authors, reloaded.Authors);
        Assert.Equal(original.Abstract, reloaded.Abstract);
        Assert.Equal(original.Description, reloaded.Description);
        Assert.Equal(original.License, reloaded.License);
        Assert.Equal(original.Tags, reloaded.Tags);
        Assert.Equal(ModStatus.Deprecated, reloaded.Status);
        Assert.Equal(original.SupersededBy, reloaded.SupersededBy);
        Assert.Equal(original.ForumUrl, reloaded.ForumUrl);
        Assert.Equal(original.Links["homepage"], reloaded.Links["homepage"]);
        Assert.Equal(original.GameMin, reloaded.GameMin);
        Assert.Equal(original.GameMax, reloaded.GameMax);
        Assert.Equal(original.Os, reloaded.Os);
        Assert.Equal(original.Version, reloaded.Version);
        Assert.Equal(original.ReleasedAt, reloaded.ReleasedAt);
        Assert.Equal(original.Changelog, reloaded.Changelog);

        Assert.Equal(2, reloaded.Mods.Count);
        Assert.Equal("AdvancedFlightComputer", reloaded.Mods[0].ContentId);
        Assert.Equal(ModVersion.Parse("0.7.0"), reloaded.Mods[0].Version);
        Assert.Equal("ReusableBoosterDemo", reloaded.Vehicles[0].ContentId);
        Assert.Equal("ApolloRecreation", reloaded.Saves[0].ContentId);

        Assert.Contains("Description", tomlText);
        Assert.Contains("SupersededBy", tomlText);
        Assert.Contains("GameMax", tomlText);
        Assert.Contains("Os = ", tomlText);
        Assert.Contains("Changelog", tomlText);

        Assert.Contains("Type = \"modpack\"", tomlText);
        Assert.Contains("Status = \"deprecated\"", tomlText);
    }

    [Fact]
    public async Task RoundTrip_MinimalShape_AbsentOptionalsStayAbsent()
    {
        var original = MinimalPack();

        var (reloaded, reloadedDto, tomlText) = await RoundTripAsync(original);

        Assert.Null(reloadedDto.Description);
        Assert.Null(reloadedDto.SupersededBy);
        Assert.Null(reloadedDto.GameMax);
        Assert.Null(reloadedDto.Os);
        Assert.Null(reloadedDto.Changelog);

        Assert.Null(reloaded.Description);
        Assert.Null(reloaded.GameMax);
        Assert.Null(reloaded.Os);
        Assert.Null(reloaded.Changelog);
        Assert.Equal(ModStatus.Active, reloaded.Status);
        Assert.Empty(reloaded.Tags);
        Assert.Empty(reloaded.Vehicles);
        Assert.Empty(reloaded.Saves);

        Assert.DoesNotContain("Description", tomlText);
        Assert.DoesNotContain("SupersededBy", tomlText);
        Assert.DoesNotContain("GameMax", tomlText);
        Assert.DoesNotContain("Os = ", tomlText);
        Assert.DoesNotContain("Changelog", tomlText);
    }

    [Fact]
    public async Task RoundTrip_TimestampKeepsSubSecondPrecision()
    {
        var (reloaded, _, _) = await RoundTripAsync(MinimalPack());

        Assert.Equal(SampleTimestamp().Ticks, reloaded.ReleasedAt.Ticks);
    }

    [Fact]
    public async Task RoundTrip_MonthFormGameMin_StaysAsAuthored()
    {
        var (reloaded, _, _) = await RoundTripAsync(MinimalPack());

        Assert.Equal("2026.7", reloaded.GameMin);
    }

    [Fact]
    public void FromDto_AbsentStatus_IsActive()
    {
        var dto = ModPackMetadataMapper.ToDto(MinimalPack());
        dto.Status = null;

        Assert.Equal(ModStatus.Active, ModPackMetadataMapper.FromDto(dto).Status);
    }

    [Fact]
    public void FromDto_MissingSpecVersion_NamesThePreModelShape()
    {
        var dto = ModPackMetadataMapper.ToDto(MinimalPack());
        dto.SpecVersion = 0;

        var exception = Assert.Throws<FormatException>(() => ModPackMetadataMapper.FromDto(dto));
        Assert.Contains("spec version", exception.Message);
    }

    [Fact]
    public void FromDto_NegativeSpecVersion_IsAMalformedFile()
    {
        var dto = ModPackMetadataMapper.ToDto(MinimalPack());
        dto.SpecVersion = -3;

        var exception = Assert.Throws<FormatException>(() => ModPackMetadataMapper.FromDto(dto));
        Assert.Contains("spec version", exception.Message);
    }

    [Fact]
    public void FromDto_SpecVersionAboveHighest_StillMaps()
    {
        var dto = ModPackMetadataMapper.ToDto(MinimalPack());
        dto.SpecVersion = SpecVersions.Highest + 1;

        Assert.Equal(SpecVersions.Highest + 1, ModPackMetadataMapper.FromDto(dto).SpecVersion);
    }

    [Theory]
    [InlineData("mod")]
    [InlineData("mod-loader")]
    [InlineData("vehicle")]
    public void FromDto_TypeThatIsNotAPack_IsRefused(string type)
    {
        var dto = ModPackMetadataMapper.ToDto(MinimalPack());
        dto.Type = type;

        var exception = Assert.Throws<FormatException>(() => ModPackMetadataMapper.FromDto(dto));
        Assert.Contains("not a pack", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
