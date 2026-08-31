using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Mods;
using Borea.Storage.Toml;

namespace Borea.Storage.Tests.Mods;

public sealed class ModMetadataMapperTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private async Task<(ModMetadata Reloaded, ModMetadataDto ReloadedDto, string TomlText)> RoundTripAsync(ModMetadata original)
    {
        var path = Path.Combine(_tempRoot, "metadata.toml");
        await TomlFileStore.WriteAsync(path, ModMetadataMapper.ToDto(original));
        var reloadedDto = await TomlFileStore.ReadAsync<ModMetadataDto>(path);
        return (ModMetadataMapper.FromDto(reloadedDto!), reloadedDto!, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RoundTrip_FullShape_PreservesEveryField()
    {
        var original = MetadataFixtures.FullMetadata();

        var (reloaded, _, tomlText) = await RoundTripAsync(original);

        Assert.Equal(original.SpecVersion, reloaded.SpecVersion);
        Assert.Equal(original.ModId, reloaded.ModId);
        Assert.Equal(original.Type, reloaded.Type);
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
        Assert.Equal(original.Links["repository"], reloaded.Links["repository"]);
        Assert.Equal(original.GameMin, reloaded.GameMin);
        Assert.Equal(original.GameMax, reloaded.GameMax);
        Assert.Equal(original.Os, reloaded.Os);
        Assert.Equal(original.Install!.Root, reloaded.Install!.Root);
        Assert.Equal(original.Install.Manages, reloaded.Install.Manages);
        Assert.Equal(original.Install.Steps, reloaded.Install.Steps);
        // Absent is not empty.
        Assert.Empty(reloaded.Install.Uninstall!);

        Assert.NotNull(reloaded.Releases);
        Assert.Equal("github", reloaded.Releases!.Authority);
        Assert.Equal(2, reloaded.Releases.Hosts.Count);
        Assert.Equal("owner/repo", reloaded.Releases.AuthorityHost.Reference);

        Assert.NotNull(reloaded.Loader);
        Assert.Equal(original.Loader!.LoaderId, reloaded.Loader!.LoaderId);
        Assert.Equal(original.Loader.MinVersion, reloaded.Loader.MinVersion);
        Assert.Equal(original.Loader.MaxVersion, reloaded.Loader.MaxVersion);

        Assert.Equal(3, reloaded.Dependencies.Count);
        Assert.Equal(original.Dependencies[0].MinVersion, reloaded.Dependencies[0].MinVersion);
        Assert.Equal(original.Dependencies[1].Kind, reloaded.Dependencies[1].Kind);
        Assert.True(reloaded.Dependencies[2].IsAnyOf);
        Assert.Equal(2, reloaded.Dependencies[2].AnyOf!.Count);
        Assert.Null(reloaded.Dependencies[2].AnyOf![1].MinVersion);

        // The optional keys are really in the file when set, proving the
        // absence assertions in the minimal test can fail.
        Assert.Contains("Description", tomlText);
        Assert.Contains("SupersededBy", tomlText);
        Assert.Contains("GameMax", tomlText);
        Assert.Contains("Os = ", tomlText);
        Assert.Contains("[Install]", tomlText);

        // The enum vocabulary on disk stays the format's lowercase spelling.
        Assert.Contains("Status = \"deprecated\"", tomlText);
        Assert.Contains("Kind = \"conflict\"", tomlText);
        Assert.Contains("Kind = \"recommends\"", tomlText);
        Assert.Contains("Type = \"mod\"", tomlText);
    }

    [Fact]
    public async Task RoundTrip_MinimalShape_AbsentOptionalsStayAbsent()
    {
        var original = MetadataFixtures.MinimalMetadata();

        var (reloaded, reloadedDto, tomlText) = await RoundTripAsync(original);

        Assert.Null(reloadedDto.Description);
        Assert.Null(reloadedDto.SupersededBy);
        Assert.Null(reloadedDto.Releases);
        Assert.Null(reloadedDto.GameMax);
        Assert.Null(reloadedDto.Os);
        Assert.Null(reloadedDto.Loader);
        Assert.Null(reloadedDto.Install);
        Assert.Null(reloadedDto.Provides);

        Assert.Null(reloaded.Description);
        Assert.Null(reloaded.GameMax);
        Assert.Null(reloaded.Os);
        Assert.Empty(reloaded.Dependencies);
        Assert.Empty(reloaded.Tags);
        Assert.Equal(ModStatus.Active, reloaded.Status);

        Assert.DoesNotContain("Description", tomlText);
        Assert.DoesNotContain("SupersededBy", tomlText);
        Assert.DoesNotContain("GameMax", tomlText);
        Assert.DoesNotContain("Os = ", tomlText);
        Assert.DoesNotContain("[Install]", tomlText);
    }

    [Fact]
    public async Task RoundTrip_EmptyOsList_StaysEmptyInsteadOfAbsent()
    {
        var original = new ModMetadata(
            specVersion: 1,
            modId: "test-mod",
            source: "TestSource",
            name: "Test Mod",
            authors: new[] { "Author" },
            abstractText: "Abstract.",
            license: "MIT",
            links: MetadataFixtures.SampleLinks(),
            gameMin: "2026.7",
            os: Array.Empty<string>());

        var (reloaded, _, _) = await RoundTripAsync(original);

        Assert.NotNull(reloaded.Os);
        Assert.Empty(reloaded.Os!);
    }

    [Fact]
    public void FromDto_AbsentStatus_IsActive()
    {
        var dto = ModMetadataMapper.ToDto(MetadataFixtures.MinimalMetadata());
        dto.Status = null;

        Assert.Equal(ModStatus.Active, ModMetadataMapper.FromDto(dto).Status);
    }

    [Fact]
    public void FromDto_UnknownStatusAndType_ParseToUnknown()
    {
        var dto = ModMetadataMapper.ToDto(MetadataFixtures.MinimalMetadata());
        dto.Status = "frozen";
        dto.Type = "vehicle";

        var reloaded = ModMetadataMapper.FromDto(dto);

        Assert.Equal(ModStatus.Unknown, reloaded.Status);
        Assert.Equal(ContentType.Unknown, reloaded.Type);
    }

    [Fact]
    public async Task RoundTrip_TheStarMapListing_KeepsInstallAndProvides()
    {
        var original = new ModMetadata(
            specVersion: 1,
            modId: "StarMap",
            source: "TestSource",
            name: "StarMap",
            authors: new[] { "KlaasWhite" },
            abstractText: "Mod loader that runs code mods.",
            license: "MIT",
            links: MetadataFixtures.SampleLinks(),
            gameMin: "2026.8.3.5117",
            type: ContentType.ModLoader,
            install: new InstallDescriptor(
                target: InstallAnchor.Standalone,
                uninstall: new[] { "Delete the StarMap directory." }),
            provides: new LoaderProvides(
                launch: "StarMap.exe",
                contentDir: InstallAnchor.Mods,
                configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation")));

        var (reloaded, _, tomlText) = await RoundTripAsync(original);

        Assert.Equal(InstallAnchor.Standalone, reloaded.Install!.Target);
        Assert.Single(reloaded.Install.Uninstall!);
        Assert.Null(reloaded.Install.Steps);
        Assert.Equal("StarMap.exe", reloaded.Provides!.Launch);
        Assert.Equal(InstallAnchor.Mods, reloaded.Provides.ContentDir);
        Assert.Equal(ConfigureFormat.Json, reloaded.Provides.Configure!.Format);
        Assert.Equal("GameLocation", reloaded.Provides.Configure.GamePath);

        Assert.Contains("Target = \"standalone\"", tomlText);
        Assert.Contains("ContentDir = \"mods\"", tomlText);
        Assert.Contains("Format = \"json\"", tomlText);
    }

    [Fact]
    public void FromDto_UnknownInstallTarget_ParsesToUnknown()
    {
        var dto = ModMetadataMapper.ToDto(MetadataFixtures.FullMetadata());
        dto.Install!.Target = "somewhere-new";

        Assert.Equal(InstallAnchor.Unknown, ModMetadataMapper.FromDto(dto).Install!.Target);
    }

    [Fact]
    public void FromDto_MissingSpecVersion_NamesThePreModelShape()
    {
        var dto = ModMetadataMapper.ToDto(MetadataFixtures.MinimalMetadata());
        dto.SpecVersion = 0;

        var exception = Assert.Throws<FormatException>(() => ModMetadataMapper.FromDto(dto));
        Assert.Contains("spec version", exception.Message);
    }

    [Fact]
    public void FromDto_NegativeSpecVersion_IsAMalformedFile()
    {
        var dto = ModMetadataMapper.ToDto(MetadataFixtures.MinimalMetadata());
        dto.SpecVersion = -3;

        var exception = Assert.Throws<FormatException>(() => ModMetadataMapper.FromDto(dto));
        Assert.Contains("spec version", exception.Message);
    }

    [Fact]
    public void FromDto_SpecVersionAboveHighest_StillMaps()
    {
        var dto = ModMetadataMapper.ToDto(MetadataFixtures.MinimalMetadata());
        dto.SpecVersion = SpecVersions.Highest + 1;

        var mapped = ModMetadataMapper.FromDto(dto);

        Assert.Equal(SpecVersions.Highest + 1, mapped.SpecVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
