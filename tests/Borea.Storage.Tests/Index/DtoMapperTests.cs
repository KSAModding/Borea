using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Index;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Tests.Index;

public sealed class DtoMapperTests
{
    // ---- DTO builders: only the required fields are set unless a test overrides one. ----

    private static LinksDto Links(string forums = "https://forums.example/thread/1") =>
        new() { Forums = forums };

    private static CompatibilityDto Compatibility(string gameMin = "2026.7.4.2131", string? gameMax = null, List<string>? os = null) =>
        new() { GameMin = gameMin, GameMax = gameMax, Os = os };

    private static AuthoredDto MinimalAuthoredDto(string id = "test-mod", string type = "mod") => new()
    {
        SpecVersion = 1,
        Id = id,
        Type = type,
        Name = "Test Mod",
        Authors = new List<string> { "Test Author" },
        Abstract = "A mod used for testing.",
        License = "MIT",
        Compatibility = Compatibility(),
        Links = Links(),
    };

    private static DownloadInfoDto Download() => new()
    {
        URL = "https://example.com/mod.zip",
        SHA256 = new string('A', 64),
        Size = 1024,
        ContentType = "application/zip",
    };

    private static ReleasesEntryDto MinimalReleaseDto(string id = "test-mod") => new()
    {
        SpecVersion = 1,
        Id = id,
        Type = "mod",
        Version = "1.0.0",
        VersionScheme = "semver",
        ReleaseStatus = "stable",
        ReleaseDate = "2026-08-08T12:00:00Z",
        GameMin = "2026.7.4.2131",
        GameMinRevision = 2131,
        Download = Download(),
        InstallSize = 2048,
        Dependencies = new List<DependencyEntryDto>(),
    };

    private static PackAuthoredDto MinimalPackAuthoredDto(string id = "test-pack") => new()
    {
        SpecVersion = 1,
        Id = id,
        Type = "modpack",
        Name = "Test Pack",
        Authors = new List<string> { "Pack Author" },
        Abstract = "Pack abstract.",
        License = "CC0-1.0",
        Version = "1.0.0",
        ReleasedAt = "2026-08-08T12:00:00Z",
        Links = Links(),
        Compatibility = Compatibility(gameMin: "2026.7"),
        Mods = new List<IndexModPackItemEntryDto>
        {
            new() { Id = "some-mod", Version = "1.0.0" },
        },
    };

    // ---- MapAuthored ----

    [Fact]
    public void MapAuthored_MinimalDto_MapsRequiredFields()
    {
        var dto = MinimalAuthoredDto();

        var result = DtoMapper.MapAuthored(dto, "test-source");

        Assert.Equal(1, result.SpecVersion);
        Assert.Equal("test-mod", result.ModId);
        Assert.Equal(ContentType.Mod, result.Type);
        Assert.Equal("test-source", result.Source);
        Assert.Equal("Test Mod", result.Name);
        Assert.Equal(new[] { "Test Author" }, result.Authors);
        Assert.Equal("A mod used for testing.", result.Abstract);
        Assert.Equal("MIT", result.License);
        Assert.Equal("https://forums.example/thread/1", result.ForumUrl);
        Assert.Equal("2026.7.4.2131", result.GameMin);
        Assert.Equal(ModStatus.Active, result.Status);
        Assert.Null(result.Description);
        Assert.Null(result.SupersededBy);
        Assert.Null(result.Releases);
        Assert.Null(result.GameMax);
        Assert.Null(result.Os);
        Assert.Null(result.Loader);
        Assert.Empty(result.Dependencies);
        Assert.Null(result.Install);
        Assert.Null(result.Provides);
    }

    // Skips modpack since they have their own content type
    [Theory]
    [InlineData("mod", ContentType.Mod)]
    [InlineData("mod-loader", ContentType.ModLoader)]
    [InlineData("something-else", ContentType.Unknown)]
    public void MapAuthored_MapsContentType(string type, ContentType expected)
    {
        var dto = MinimalAuthoredDto(type: type);

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.Equal(expected, result.Type);
    }

    [Theory]
    [InlineData(null, ModStatus.Active)]
    [InlineData("active", ModStatus.Active)]
    [InlineData("deprecated", ModStatus.Deprecated)]
    [InlineData("something-weird", ModStatus.Unknown)]
    public void MapAuthored_MapsStatus(string? status, ModStatus expected)
    {
        var dto = MinimalAuthoredDto();
        dto.Status = status;

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void MapAuthored_Links_IncludesExtraStringLinksAndSkipsNonStringOnes()
    {
        var dto = MinimalAuthoredDto();
        dto.Links.OtherLinks = new Dictionary<string, JsonElement>
        {
            ["discord"] = JsonSerializer.SerializeToElement("https://discord.example/invite"),
            ["weird"] = JsonSerializer.SerializeToElement(12345),
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.Equal("https://discord.example/invite", result.Links["discord"]);
        Assert.False(result.Links.ContainsKey("weird"));
        Assert.Equal("https://forums.example/thread/1", result.Links["forums"]);
    }

    [Fact]
    public void MapAuthored_ReleaseSource_MapsStringAndNumericHostReferences()
    {
        var dto = MinimalAuthoredDto();
        dto.Releases = new ReleasesInfoDto
        {
            Authority = "github",
            Hosts = new Dictionary<string, JsonElement>
            {
                ["github"] = JsonSerializer.SerializeToElement("owner/repo"),
                ["spacedock"] = JsonSerializer.SerializeToElement(4242),
            },
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.NotNull(result.Releases);
        Assert.Equal("github", result.Releases!.Authority);
        Assert.Equal("owner/repo", result.Releases.Hosts.Single(h => h.Host == "github").Reference);
        Assert.Equal("4242", result.Releases.Hosts.Single(h => h.Host == "spacedock").Reference);
    }

    [Fact]
    public void MapAuthored_ReleaseSource_NoAuthority_DefaultsToSoleHost()
    {
        var dto = MinimalAuthoredDto();
        dto.Releases = new ReleasesInfoDto
        {
            Hosts = new Dictionary<string, JsonElement>
            {
                ["github"] = JsonSerializer.SerializeToElement("owner/repo"),
            },
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.Equal("github", result.Releases!.Authority);
    }

    [Fact]
    public void MapAuthored_ReleaseSource_PreservesUnknownAuthorityHost()
    {
        var dto = MinimalAuthoredDto();
        dto.Releases = new ReleasesInfoDto
        {
            Authority = "future-host",
            Hosts = new Dictionary<string, JsonElement>
            {
                ["future-host"] = JsonSerializer.SerializeToElement("publisher/project"),
                ["github"] = JsonSerializer.SerializeToElement("owner/repo"),
            },
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.Equal("future-host", result.Releases!.Authority);
        Assert.Equal("publisher/project", result.Releases.AuthorityHost.Reference);
        Assert.Equal(2, result.Releases.Hosts.Count);
    }

    [Fact]
    public void MapAuthored_ReleaseSource_ThrowsForNonStringNonNumberHost()
    {
        var dto = MinimalAuthoredDto();
        dto.Releases = new ReleasesInfoDto
        {
            Hosts = new Dictionary<string, JsonElement>
            {
                ["weird"] = JsonSerializer.SerializeToElement(new { nested = true }),
            },
        };

        Assert.Throws<FormatException>(() => DtoMapper.MapAuthored(dto, "source"));
    }

    [Theory]
    [InlineData("required", ModDependencyKind.Required)]
    [InlineData("optional", ModDependencyKind.Optional)]
    [InlineData("recommends", ModDependencyKind.Recommends)]
    [InlineData("suggests", ModDependencyKind.Suggests)]
    [InlineData("conflict", ModDependencyKind.Conflict)]
    [InlineData("something-else", ModDependencyKind.Unknown)]
    public void MapAuthored_MapsDependencyKind(string kind, ModDependencyKind expected)
    {
        var dto = MinimalAuthoredDto();
        dto.Dependencies = new List<DependencyEntryDto> { new() { Id = "other-mod", Kind = kind } };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.Equal(expected, Assert.Single(result.Dependencies).Kind);
    }

    [Fact]
    public void MapAuthored_Dependency_SingleId_MapsBoundsAndSource()
    {
        var dto = MinimalAuthoredDto();
        dto.Dependencies = new List<DependencyEntryDto>
        {
            new() { Id = "other-mod", Kind = "required", Min = "1.0.0", Max = "2.0.0", Source = "authored" },
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        var dependency = Assert.Single(result.Dependencies);
        Assert.False(dependency.IsAnyOf);
        Assert.Equal("other-mod", dependency.ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), dependency.MinVersion);
        Assert.Equal(ModVersion.Parse("2.0.0"), dependency.MaxVersion);
        Assert.Equal(MetadataSource.Authored, dependency.Source);
    }

    [Fact]
    public void MapAuthored_Dependency_AnyOf_MapsAlternatives()
    {
        var dto = MinimalAuthoredDto();
        dto.Dependencies = new List<DependencyEntryDto>
        {
            new()
            {
                Kind = "recommends",
                AnyOf = new List<AnyOfDependencyDto>
                {
                    new() { Id = "mod-a", Min = "1.0.0" },
                    new() { Id = "mod-b" },
                },
            },
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        var dependency = Assert.Single(result.Dependencies);
        Assert.True(dependency.IsAnyOf);
        Assert.Equal(ModDependencyKind.Recommends, dependency.Kind);
        Assert.Equal(2, dependency.AnyOf!.Count);
        Assert.Equal("mod-a", dependency.AnyOf[0].ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), dependency.AnyOf[0].MinVersion);
        Assert.Equal("mod-b", dependency.AnyOf[1].ModId);
        Assert.Null(dependency.AnyOf[1].MinVersion);
    }

    [Fact]
    public void MapAuthored_Loader_MapsRequirement()
    {
        var dto = MinimalAuthoredDto();
        dto.Loader = new LoaderDto { Id = "test-loader", Min = "1.0.0", Max = "2.0.0", Source = "derived" };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.NotNull(result.Loader);
        Assert.Equal("test-loader", result.Loader!.LoaderId);
        Assert.Equal(ModVersion.Parse("1.0.0"), result.Loader.MinVersion);
        Assert.Equal(ModVersion.Parse("2.0.0"), result.Loader.MaxVersion);
        Assert.Equal(MetadataSource.Derived, result.Loader.Source);
    }

    [Fact]
    public void MapAuthored_InstallDescriptor_MapsFields()
    {
        var dto = MinimalAuthoredDto();
        dto.Install = new InstallDescriptorDto
        {
            Root = "content",
            Target = "mods",
            Path = "sub",
            Manages = new List<string> { "config.cfg" },
            Steps = new List<string> { "do a thing" },
            Uninstall = new List<string> { "undo a thing" },
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.NotNull(result.Install);
        Assert.Equal("content", result.Install!.Root);
        Assert.Equal(InstallAnchor.Mods, result.Install.Target);
        Assert.Equal("sub", result.Install.Path);
        Assert.Equal(new[] { "config.cfg" }, result.Install.Manages);
        Assert.Equal(new[] { "do a thing" }, result.Install.Steps);
        Assert.Equal(new[] { "undo a thing" }, result.Install.Uninstall);
    }

    [Fact]
    public void MapAuthored_StandaloneInstallTarget_MapsWhenProvidesLaunchIsSet()
    {
        // ModMetadata requires provides.Launch whenever install.Target is
        // Standalone, and Provides is only legal on a mod-loader listing.
        var dto = MinimalAuthoredDto(type: "mod-loader");
        dto.Install = new InstallDescriptorDto { Target = "standalone" };
        dto.Provides = new ProvidesDto { Launch = "bin/run.exe" };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.Equal(InstallAnchor.Standalone, result.Install!.Target);
        Assert.Equal("bin/run.exe", result.Provides!.Launch);
    }

    [Fact]
    public void MapAuthored_Provides_MapsLoaderConfigure()
    {
        var dto = MinimalAuthoredDto(type: "mod-loader");
        dto.Provides = new ProvidesDto
        {
            Launch = "bin/loader.exe",
            ContentDirectory = "mods",
            ContentPath = "plugins",
            Configure = new ConfigureDto { File = "config.json", Format = "json", GamePath = "paths.game" },
        };

        var result = DtoMapper.MapAuthored(dto, "source");

        Assert.NotNull(result.Provides);
        Assert.Equal("bin/loader.exe", result.Provides!.Launch);
        Assert.Equal(InstallAnchor.Mods, result.Provides.ContentDir);
        Assert.Equal("plugins", result.Provides.ContentPath);
        Assert.NotNull(result.Provides.Configure);
        Assert.Equal("config.json", result.Provides.Configure!.File);
        Assert.Equal(ConfigureFormat.Json, result.Provides.Configure.Format);
        Assert.Equal("paths.game", result.Provides.Configure.GamePath);
    }

    [Fact]
    public void MapAuthored_UnknownConfigureMember_Throws()
    {
        var dto = MinimalAuthoredDto(type: "mod-loader");
        dto.Provides = new ProvidesDto
        {
            Configure = new ConfigureDto
            {
                File = "config.json",
                Format = "json",
                UnknownFields = new Dictionary<string, JsonElement>
                {
                    ["future-value"] = JsonSerializer.SerializeToElement("value"),
                },
            },
        };

        var exception = Assert.Throws<FormatException>(() => DtoMapper.MapAuthored(dto, "source"));

        Assert.Contains("future-value", exception.Message);
    }

    [Fact]
    public void MapAuthored_UnknownConfigureFormat_Throws()
    {
        var dto = MinimalAuthoredDto(type: "mod-loader");
        dto.Provides = new ProvidesDto
        {
            Configure = new ConfigureDto { File = "config.data", Format = "future-format" },
        };

        Assert.Throws<FormatException>(() => DtoMapper.MapAuthored(dto, "source"));
    }

    [Fact]
    public void MapAuthored_NullCollectionElements_Throw()
    {
        var dto = MinimalAuthoredDto();
        dto.Authors.Add(null!);

        Assert.Throws<FormatException>(() => DtoMapper.MapAuthored(dto, "source"));
    }

    [Fact]
    public void MapAuthored_NullInstallStep_Throws()
    {
        var dto = MinimalAuthoredDto();
        dto.Install = new InstallDescriptorDto { Steps = new List<string> { null! } };

        Assert.Throws<FormatException>(() => DtoMapper.MapAuthored(dto, "source"));
    }

    // ---- MapRelease ----

    [Fact]
    public void MapRelease_MinimalDto_MapsRequiredFields()
    {
        var dto = MinimalReleaseDto();

        var result = DtoMapper.MapRelease(dto, "test-source", authored: null);

        Assert.Equal(1, result.SpecVersion);
        Assert.Equal("test-mod", result.ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), result.Version);
        Assert.Equal(ReleaseStatus.Stable, result.ReleaseStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-08-08T12:00:00Z"), result.ReleaseDate);
        Assert.Equal("2026.7.4.2131", result.GameMin);
        Assert.Equal(2131, result.GameMinRevision);
        Assert.Equal("https://example.com/mod.zip", result.Download.Url);
        Assert.Equal(2048, result.InstallSizeBytes);
        Assert.Empty(result.Dependencies);
        Assert.Equal(ContentType.Mod, result.Type);
        Assert.False(result.Yanked);
        Assert.Null(result.Listing);
        Assert.Equal("test-source", result.Source);
    }

    [Theory]
    [InlineData("stable", ReleaseStatus.Stable)]
    [InlineData("testing", ReleaseStatus.Testing)]
    [InlineData("dev", ReleaseStatus.Dev)]
    [InlineData("weird", ReleaseStatus.Unknown)]
    public void MapRelease_MapsReleaseStatus(string status, ReleaseStatus expected)
    {
        var dto = MinimalReleaseDto();
        dto.ReleaseStatus = status;

        var result = DtoMapper.MapRelease(dto, null, authored: null);

        Assert.Equal(expected, result.ReleaseStatus);
    }

    [Fact]
    public void MapRelease_YankedFields_MapThrough()
    {
        var dto = MinimalReleaseDto();
        dto.Yanked = true;
        dto.YankedReason = "Broke saves";

        var result = DtoMapper.MapRelease(dto, null, authored: null);

        Assert.True(result.Yanked);
        Assert.Equal("Broke saves", result.YankedReason);
    }

    [Fact]
    public void MapRelease_InstallAndLoader_AreMapped()
    {
        var dto = MinimalReleaseDto();
        dto.Install = new InstallInfoDto { Root = "content", Derived = true, Target = "user-data", Path = "saves" };
        dto.Loader = new LoaderDto { Id = "test-loader", Min = "1.0.0", Source = "authored" };

        var result = DtoMapper.MapRelease(dto, null, authored: null);

        Assert.NotNull(result.Install);
        Assert.Equal("content", result.Install!.Root);
        Assert.True(result.Install.Derived);
        Assert.Equal(InstallAnchor.UserData, result.Install.Target);
        Assert.Equal("saves", result.Install.Path);

        Assert.NotNull(result.Loader);
        Assert.Equal("test-loader", result.Loader!.LoaderId);
        Assert.Equal(ModVersion.Parse("1.0.0"), result.Loader.MinVersion);
        Assert.Null(result.Loader.MaxVersion);
        Assert.Equal(MetadataSource.Authored, result.Loader.Source);
    }

    [Theory]
    [InlineData("mods", InstallAnchor.Mods)]
    [InlineData("user-data", InstallAnchor.UserData)]
    [InlineData("game-root", InstallAnchor.GameRoot)]
    [InlineData("standalone", InstallAnchor.Standalone)]
    public void MapRelease_MapsInstallAnchor(string target, InstallAnchor expected)
    {
        var dto = MinimalReleaseDto();
        dto.Install = new InstallInfoDto { Derived = false, Target = target };

        var result = DtoMapper.MapRelease(dto, null, authored: null);

        Assert.Equal(expected, result.Install!.Target);
    }

    [Fact]
    public void MapRelease_UnknownInstallAnchor_Throws()
    {
        var dto = MinimalReleaseDto();
        dto.Install = new InstallInfoDto { Derived = false, Target = "something-else" };

        Assert.Throws<FormatException>(() => DtoMapper.MapRelease(dto, null, authored: null));
    }

    [Fact]
    public void MapRelease_Dependency_ConflictKind_MapsWithoutRequiringBounds()
    {
        var dto = MinimalReleaseDto();
        dto.Dependencies = new List<DependencyEntryDto>
        {
            new() { Id = "incompatible-mod", Kind = "conflict" },
        };

        var result = DtoMapper.MapRelease(dto, null, authored: null);

        var dependency = Assert.Single(result.Dependencies);
        Assert.Equal(ModDependencyKind.Conflict, dependency.Kind);
        Assert.Null(dependency.MinVersion);
        Assert.Null(dependency.MaxVersion);
    }

    [Fact]
    public void MapRelease_NullMirror_Throws()
    {
        var dto = MinimalReleaseDto();
        dto.Download.Mirrors = new List<string> { null! };

        Assert.Throws<FormatException>(() => DtoMapper.MapRelease(dto, null, authored: null));
    }

    [Fact]
    public void MapRelease_NullDependencyAlternative_Throws()
    {
        var dto = MinimalReleaseDto();
        dto.Dependencies.Add(new DependencyEntryDto
        {
            Kind = "required",
            AnyOf = new List<AnyOfDependencyDto> { null! },
        });

        Assert.Throws<FormatException>(() => DtoMapper.MapRelease(dto, null, authored: null));
    }

    // ---- MapRelease: listing snapshot merge behavior ----

    [Fact]
    public void MapRelease_NoListingKey_LeavesSnapshotNull()
    {
        var dto = MinimalReleaseDto();
        dto.Listing = null;
        var authored = DtoMapper.MapAuthored(MinimalAuthoredDto(), "source");

        var result = DtoMapper.MapRelease(dto, null, authored);

        Assert.Null(result.Listing);
    }

    [Fact]
    public void MapRelease_CompleteListingSnapshot_UsesReleasesOwnValues()
    {
        var dto = MinimalReleaseDto();
        dto.Listing = JsonSerializer.SerializeToElement(new
        {
            name = "Release-Time Name",
            authors = new[] { "Release Author" },
            @abstract = "Release-time abstract.",
            description = "Release-time description.",
            license = "Apache-2.0",
            tags = new[] { "release-tag" },
            links = new { forums = "https://forums.example/release-thread" },
        });
        var authored = DtoMapper.MapAuthored(MinimalAuthoredDto(), "source");

        var result = DtoMapper.MapRelease(dto, null, authored);

        Assert.NotNull(result.Listing);
        Assert.Equal("Release-Time Name", result.Listing!.Name);
        Assert.Equal(new[] { "Release Author" }, result.Listing.Authors);
        Assert.Equal("Release-time abstract.", result.Listing.Abstract);
        Assert.Equal("Release-time description.", result.Listing.Description);
        Assert.Equal("Apache-2.0", result.Listing.License);
        Assert.Equal(new[] { "release-tag" }, result.Listing.Tags);
        Assert.Equal("https://forums.example/release-thread", result.Listing.Links["forums"]);
    }

    [Fact]
    public void MapRelease_PartialListingSnapshot_FallsBackToAuthoredPerField()
    {
        var dto = MinimalReleaseDto();
        // Only overrides the name; every other field should fall back to authored.
        dto.Listing = JsonSerializer.SerializeToElement(new { name = "Release-Time Name" });
        var authored = DtoMapper.MapAuthored(MinimalAuthoredDto(), "source");

        var result = DtoMapper.MapRelease(dto, null, authored);

        Assert.NotNull(result.Listing);
        Assert.Equal("Release-Time Name", result.Listing!.Name);
        Assert.Equal(authored.Authors, result.Listing.Authors);
        Assert.Equal(authored.Abstract, result.Listing.Abstract);
        Assert.Equal(authored.License, result.Listing.License);
        Assert.Equal(authored.Description, result.Listing.Description);
        Assert.Equal(authored.Links["forums"], result.Listing.Links["forums"]);
    }

    [Fact]
    public void MapRelease_EmptyAuthorsArray_Throws()
    {
        var dto = MinimalReleaseDto();
        dto.Listing = JsonSerializer.SerializeToElement(new { authors = Array.Empty<string>() });
        var authored = DtoMapper.MapAuthored(MinimalAuthoredDto(), "source");

        Assert.ThrowsAny<ArgumentException>(() => DtoMapper.MapRelease(dto, null, authored));
    }

    [Fact]
    public void MapRelease_EmptyTagsArray_IsKeptRatherThanFallingBackToAuthored()
    {
        // Tags only falls back when null (dto?.Tags ?? authored.Tags), unlike
        // Authors: an explicit empty tag list on the release is a real value,
        // not treated as "missing".
        var dto = MinimalReleaseDto();
        dto.Listing = JsonSerializer.SerializeToElement(new { tags = Array.Empty<string>() });
        var authoredDto = MinimalAuthoredDto();
        authoredDto.Tags = new List<string> { "authored-tag" };
        var authored = DtoMapper.MapAuthored(authoredDto, "source");

        var result = DtoMapper.MapRelease(dto, null, authored);

        Assert.Empty(result.Listing!.Tags);
    }

    [Fact]
    public void MapRelease_MalformedListingJson_Throws()
    {
        var dto = MinimalReleaseDto();
        // "authors" must be an array of strings; a number here fails to
        // deserialize as a ReleaseListingDto at all.
        dto.Listing = JsonSerializer.SerializeToElement(new { authors = 5 });
        var authored = DtoMapper.MapAuthored(MinimalAuthoredDto(), "source");

        Assert.Throws<FormatException>(() => DtoMapper.MapRelease(dto, null, authored));
    }

    [Fact]
    public void MapRelease_ListingPresentButIncomplete_NoAuthoredAvailable_Throws()
    {
        var dto = MinimalReleaseDto();
        dto.Listing = JsonSerializer.SerializeToElement(new { name = "Only A Name" });

        Assert.Throws<FormatException>(() => DtoMapper.MapRelease(dto, null, authored: null));
    }

    [Fact]
    public void MapRelease_ListingComplete_NoAuthoredAvailable_UsesItDirectly()
    {
        var dto = MinimalReleaseDto();
        dto.Listing = JsonSerializer.SerializeToElement(new
        {
            name = "Standalone Name",
            authors = new[] { "Solo Author" },
            @abstract = "Standalone abstract.",
            license = "MIT",
        });

        var result = DtoMapper.MapRelease(dto, null, authored: null);

        Assert.NotNull(result.Listing);
        Assert.Equal("Standalone Name", result.Listing!.Name);
        Assert.Equal(new[] { "Solo Author" }, result.Listing.Authors);
        Assert.Equal("Standalone abstract.", result.Listing.Abstract);
        Assert.Equal("MIT", result.Listing.License);
    }

    // ---- MapPackVersion ----

    [Fact]
    public void MapPackVersion_MinimalDto_MapsRequiredFields()
    {
        var dto = MinimalPackAuthoredDto();

        var result = DtoMapper.MapPackVersion(dto, "test-source");

        Assert.Equal(1, result.SpecVersion);
        Assert.Equal("test-pack", result.ModPackId);
        Assert.Equal("test-source", result.Source);
        Assert.Equal("Test Pack", result.Name);
        Assert.Equal(new[] { "Pack Author" }, result.Authors);
        Assert.Equal("Pack abstract.", result.Abstract);
        Assert.Equal("CC0-1.0", result.License);
        Assert.Equal("2026.7", result.GameMin);
        Assert.Equal(ModVersion.Parse("1.0.0"), result.Version);
        Assert.Equal(DateTimeOffset.Parse("2026-08-08T12:00:00Z"), result.ReleasedAt);
        var mod = Assert.Single(result.Mods);
        Assert.Equal("some-mod", mod.ContentId);
        Assert.Equal(ModVersion.Parse("1.0.0"), mod.Version);
        Assert.Empty(result.Vehicles);
        Assert.Empty(result.Saves);
    }

    [Fact]
    public void MapPackVersion_VehiclesAndSaves_AreMapped()
    {
        var dto = MinimalPackAuthoredDto();
        dto.Vehicles = new List<IndexModPackItemEntryDto> { new() { Id = "some-vehicle", Version = "2.0.0" } };
        dto.Saves = new List<IndexModPackItemEntryDto> { new() { Id = "some-save", Version = "3.0.0" } };

        var result = DtoMapper.MapPackVersion(dto, "source");

        var vehicle = Assert.Single(result.Vehicles);
        Assert.Equal("some-vehicle", vehicle.ContentId);
        Assert.Equal(ModVersion.Parse("2.0.0"), vehicle.Version);

        var save = Assert.Single(result.Saves);
        Assert.Equal("some-save", save.ContentId);
        Assert.Equal(ModVersion.Parse("3.0.0"), save.Version);
    }

    [Fact]
    public void MapPackVersion_ChangelogAndDescription_MapThrough()
    {
        var dto = MinimalPackAuthoredDto();
        dto.ChangeLog = "https://example.com/changelog";
        dto.Description = "Longer pack description.";
        dto.Tags = new List<string> { "curated" };
        dto.Status = "deprecated";
        dto.SupersededBy = "newer-pack";

        var result = DtoMapper.MapPackVersion(dto, "source");

        Assert.Equal("https://example.com/changelog", result.Changelog);
        Assert.Equal("Longer pack description.", result.Description);
        Assert.Equal(new[] { "curated" }, result.Tags);
        Assert.Equal(ModStatus.Deprecated, result.Status);
        Assert.Equal("newer-pack", result.SupersededBy);
    }

    [Fact]
    public void MapPackVersion_NullMember_Throws()
    {
        var dto = MinimalPackAuthoredDto();
        dto.Mods.Add(null!);

        Assert.Throws<FormatException>(() => DtoMapper.MapPackVersion(dto, "source"));
    }

    // ---- MapIndexStatus ----

    [Theory]
    [InlineData("delisted", IndexStatusState.Delisted)]
    [InlineData("disputed", IndexStatusState.Disputed)]
    [InlineData("retracted", IndexStatusState.Retracted)]
    [InlineData("something-else", IndexStatusState.Unknown)]
    public void MapIndexStatus_MapsState(string state, IndexStatusState expected)
    {
        var dto = new IndexStatusDto { State = state };

        var result = DtoMapper.MapIndexStatus(dto);

        Assert.Equal(expected, result.State);
        Assert.Equal(state, result.RawState);
    }

    [Fact]
    public void MapIndexStatus_ParsesSinceAndKeepsReason()
    {
        var dto = new IndexStatusDto { State = "delisted", Since = "2026-08-08T12:00:00Z", Reason = "DMCA takedown" };

        var result = DtoMapper.MapIndexStatus(dto);

        Assert.Equal(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero), result.Since);
        Assert.Equal("DMCA takedown", result.Reason);
    }

    [Fact]
    public void MapIndexStatus_UnparsableSince_Throws()
    {
        var dto = new IndexStatusDto { State = "delisted", Since = "not-a-date" };

        Assert.Throws<FormatException>(() => DtoMapper.MapIndexStatus(dto));
    }

    [Fact]
    public void MapIndexStatus_NoSince_LeavesSinceNull()
    {
        var dto = new IndexStatusDto { State = "delisted" };

        var result = DtoMapper.MapIndexStatus(dto);

        Assert.Null(result.Since);
    }
}
