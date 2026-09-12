using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.ModPacks;
using Borea.Storage.Index.Dtos;
using System.Text.Json;

namespace Borea.Storage.Index;

/// <summary>
/// Maps parsed index DTOs into the RFC 0031/0035 domain types. Each mapper
/// assumes its input already passed <see cref="SnapshotParser"/> validation.
/// <br></br><br></br>
/// All the mappers are in one file since only the index will use these.
/// </summary>
public static class DtoMapper
{
    public static ModMetadata MapAuthored(AuthoredDto dto, string source)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new ModMetadata(
            specVersion: dto.SpecVersion,
            modId: dto.Id,
            source: source,
            name: dto.Name,
            authors: dto.Authors,
            abstractText: dto.Abstract,
            license: dto.License,
            links: MapLinks(dto.Links),
            gameMin: dto.Compatibility.GameMin,
            type: MapContentType(dto.Type),
            tags: dto.Tags,
            description: dto.Description,
            status: MapModStatus(dto.Status),
            supersededBy: dto.SupersededBy,
            releases: dto.Releases is null ? null : MapReleaseSource(dto.Releases),
            gameMax: dto.Compatibility.GameMax,
            os: dto.Compatibility.Os,
            loader: dto.Loader is null ? null : MapLoaderRequirement(dto.Loader),
            dependencies: dto.Dependencies?.Select(MapDependency).ToList(),
            install: dto.Install is null ? null : MapInstallDescriptor(dto.Install),
            provides: dto.Provides is null ? null : MapProvides(dto.Provides));
    }

    // Requires the authored ModMetadata to make sure listing has all the correct info
    public static ModVersionMetadata MapRelease(ReleasesEntryDto dto, string? source, ModMetadata? authored)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new ModVersionMetadata(
            specVersion: dto.SpecVersion,
            modId: dto.Id,
            version: ModVersion.Parse(dto.Version),
            releaseStatus: MapReleaseStatus(dto.ReleaseStatus),
            releaseDate: DateTimeOffset.Parse(dto.ReleaseDate),
            gameMin: dto.GameMin,
            gameMinRevision: dto.GameMinRevision,
            download: MapDownloadInfo(dto.Download),
            installSizeBytes: dto.InstallSize,
            dependencies: dto.Dependencies.Select(MapDependency).ToList(),
            type: MapContentType(dto.Type),
            versionScheme: dto.VersionScheme,
            gameMax: dto.GameMax,
            gameMaxRevision: dto.GameMaxRevision,
            os: dto.Os,
            install: dto.Install is null ? null : MapInstallInfo(dto.Install),
            loader: dto.Loader is null ? null : MapLoaderRequirement(dto.Loader),
            changelog: dto.Changelog,
            // Absent "listing" key entirely -> no snapshot to merge, leave null.
            // Present but incomplete -> merge with the live authored data.
            listing: dto.Listing is { } listingElement ? MapListingSnapshot(listingElement, authored) : null,
            yanked: dto.Yanked ?? false,
            yankedReason: dto.YankedReason,
            source: source);
    }

    public static ModPackMetadata MapPackVersion(PackAuthoredDto dto, string source)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new ModPackMetadata(
            specVersion: dto.SpecVersion,
            modPackId: dto.Id,
            source: source,
            name: dto.Name,
            authors: dto.Authors,
            abstractText: dto.Abstract,
            license: dto.License,
            links: MapLinks(dto.Links),
            gameMin: dto.Compatibility.GameMin,
            version: ModVersion.Parse(dto.Version),
            releasedAt: DateTimeOffset.Parse(dto.ReleasedAt),
            mods: dto.Mods.Select(MapPackEntry).ToList(),
            tags: dto.Tags,
            description: dto.Description,
            status: MapModStatus(dto.Status),
            supersededBy: dto.SupersededBy,
            gameMax: dto.Compatibility.GameMax,
            os: dto.Compatibility.Os,
            changelog: dto.ChangeLog,
            vehicles: dto.Vehicles?.Select(MapPackEntry).ToList(),
            saves: dto.Saves?.Select(MapPackEntry).ToList());
    }

    public static IndexStatus MapIndexStatus(IndexStatusDto dto) =>
        new(MapIndexStatusState(dto.State), dto.Since, dto.Reason);

    // Enum / value mappings

    private static ModStatus MapModStatus(string? status) => status switch
    {
        null => ModStatus.Active,
        "active" => ModStatus.Active,
        "deprecated" => ModStatus.Deprecated,
        _ => ModStatus.Unknown,
    };

    private static ReleaseStatus MapReleaseStatus(string releaseStatus) => releaseStatus switch
    {
        "stable" => ReleaseStatus.Stable,
        "testing" => ReleaseStatus.Testing,
        "dev" => ReleaseStatus.Dev,
        _ => ReleaseStatus.Unknown,
    };

    private static ContentType MapContentType(string type) => type switch
    {
        "mod" => ContentType.Mod,
        "modpack" => ContentType.ModPack,
        "mod-loader" => ContentType.ModLoader,
        _ => ContentType.Unknown,
    };

    private static ModDependencyKind MapDependencyKind(string kind) => kind switch
    {
        "required" => ModDependencyKind.Required,
        "optional" => ModDependencyKind.Optional,
        "recommends" => ModDependencyKind.Recommends,
        "suggests" => ModDependencyKind.Suggests,
        "conflict" => ModDependencyKind.Conflict,
        _ => ModDependencyKind.Unknown,
    };

    private static IndexStatusState MapIndexStatusState(string state) => state switch
    {
        "delisted" => IndexStatusState.Delisted,
        "disputed" => IndexStatusState.Disputed,
        "retracted" => IndexStatusState.Retracted,
        _ => IndexStatusState.Unknown,
    };

    private static InstallAnchor MapInstallAnchor(string target) => target switch
    {
        "mods" => InstallAnchor.Mods,
        "user-data" => InstallAnchor.UserData,
        "game-root" => InstallAnchor.GameRoot,
        "standalone" => InstallAnchor.Standalone,
        _ => InstallAnchor.Unknown,
    };

    private static MetadataSource MapMetadataSource(string source) => source switch
    {
        "authored" => MetadataSource.Authored,
        "derived" => MetadataSource.Derived,
        _ => MetadataSource.Unknown,
    };

    private static ConfigureFormat MapConfigureFormat(string format) => format switch
    {
        "json" => ConfigureFormat.Json,
        "toml" => ConfigureFormat.Toml,
        _ => ConfigureFormat.Unknown,
    };

    // Object mappings

    private static ModPackEntry MapPackEntry(IndexModPackItemEntryDto dto) =>
        new(dto.Id, ModVersion.Parse(dto.Version));

    private static IReadOnlyDictionary<string, string> MapLinks(LinksDto dto)
    {
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["forums"] = dto.Forums,
        };

        if (dto.OtherLinks is not null)
        {
            foreach (var (key, value) in dto.OtherLinks)
            {
                // Extension-data entries are arbitrary JSON; documented
                // links are plain URL strings, so anything else is skipped
                // rather than failing the whole listing over an unrelated field.
                if (value.ValueKind == JsonValueKind.String)
                {
                    links[key] = value.GetString()!;
                }
            }
        }

        return links;
    }

    private static ReleaseSource MapReleaseSource(ReleasesInfoDto dto)
    {
        var hosts = (dto.Hosts ?? new Dictionary<string, JsonElement>())
            .Select(kvp => new ReleaseHost(kvp.Key, MapHostReference(kvp.Value)))
            .ToList();

        return new ReleaseSource(hosts, dto.Authority);
    }

    // Not sure how to handle Hosts values so I just made handlers for strings (github)
    // and integers (spacedock)
    private static string MapHostReference(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        _ => throw new FormatException(
            $"Host reference must be a string or number, but was {value.ValueKind}."),
    };

    private static ModDependency MapDependency(DependencyEntryDto dto)
    {
        var kind = MapDependencyKind(dto.Kind);
        var source = dto.Source is null ? null : (MetadataSource?)MapMetadataSource(dto.Source);

        if (dto.AnyOf is { Count: > 0 })
        {
            var alternatives = dto.AnyOf
                .Select(a => new ModDependencyAlternative(
                    a.Id,
                    a.Min is null ? null : ModVersion.Parse(a.Min),
                    a.Max is null ? null : ModVersion.Parse(a.Max)))
                .ToList();

            return ModDependency.OfAlternatives(kind, alternatives, source);
        }

        return new ModDependency(
            dto.Id!,
            kind,
            dto.Min is null ? null : ModVersion.Parse(dto.Min),
            dto.Max is null ? null : ModVersion.Parse(dto.Max),
            source);
    }

    private static LoaderRequirement MapLoaderRequirement(LoaderDto dto) =>
        new(
            dto.Id,
            ModVersion.Parse(dto.Min),
            dto.Max is null ? null : ModVersion.Parse(dto.Max),
            dto.Source is null ? null : MapMetadataSource(dto.Source));

    private static InstallDescriptor MapInstallDescriptor(InstallDescriptorDto dto) =>
        new(
            root: dto.Root,
            target: dto.Target is null ? null : MapInstallAnchor(dto.Target),
            path: dto.Path,
            manages: dto.Manages,
            steps: dto.Steps,
            uninstall: dto.Uninstall);

    private static InstallInfo MapInstallInfo(InstallInfoDto dto) =>
        new(
            root: dto.Root,
            derived: dto.Derived,
            target: dto.Target is null ? null : MapInstallAnchor(dto.Target),
            path: dto.Path);

    private static LoaderProvides MapProvides(ProvidesDto dto) =>
        new(
            launch: dto.Launch,
            contentDir: dto.ContentDirectory is null ? null : MapInstallAnchor(dto.ContentDirectory),
            contentPath: dto.ContentPath,
            configure: dto.Configure is null ? null : MapConfigure(dto.Configure));

    private static LoaderConfigure MapConfigure(ConfigureDto dto) =>
        new(dto.File, MapConfigureFormat(dto.Format), dto.GamePath);

    private static DownloadInfo MapDownloadInfo(DownloadInfoDto dto) =>
        new(dto.URL, dto.SHA256, dto.Size, dto.ContentType, dto.Mirrors);

    /// <summary>
    /// Builds the release-time listing snapshot by merging whatever the
    /// release's own "listing" JSON provides with the live authored metadata,
    /// field by field. A missing or unparseable field in the release's copy
    /// falls back to the authored value rather than discarding the whole
    /// snapshot.
    /// </summary>
    private static ListingSnapshot? MapListingSnapshot(JsonElement element, ModMetadata? authored)
    {
        ReleaseListingDto? dto;
        try
        {
            dto = element.Deserialize<ReleaseListingDto>(IndexJsonOptions.Value);
        }
        catch (JsonException)
        {
            dto = null;
        }

        if (authored is null)
        {
            // If required data is missing from both sources, return null
            if (dto is null || dto.Name is null || dto.Authors is not { Count: > 0 } ||
                dto.Abstract is null || dto.License is null)
            {
                return null;
            }

            return new ListingSnapshot(
                dto.Name,
                dto.Authors,
                dto.Abstract,
                dto.License,
                dto.Tags,
                dto.Links is null ? null : MapLinks(dto.Links),
                dto.Description);
        }

        return new ListingSnapshot(
            dto?.Name ?? authored.Name,
            dto?.Authors is { Count: > 0 } ? dto.Authors : authored.Authors,
            dto?.Abstract ?? authored.Abstract,
            dto?.License ?? authored.License,
            dto?.Tags ?? authored.Tags,
            dto?.Links is null ? authored.Links : MapLinks(dto.Links),
            dto?.Description ?? authored.Description);
    }
}
