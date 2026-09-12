using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.ModPacks;
using Borea.Storage.Index.Dtos;
using System.Text.Json;
using MetadataEnumMapper = Borea.Storage.Mods.MetadataEnumMapper;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return MapInput(() => new ModMetadata(
            specVersion: dto.SpecVersion,
            modId: dto.Id,
            source: source,
            name: dto.Name,
            authors: RequireItems(dto.Authors, "authors")!,
            abstractText: dto.Abstract,
            license: dto.License,
            links: MapLinks(dto.Links),
            gameMin: dto.Compatibility.GameMin,
            type: MapContentType(dto.Type),
            tags: RequireItems(dto.Tags, "tags"),
            description: dto.Description,
            status: MapModStatus(dto.Status),
            supersededBy: dto.SupersededBy,
            releases: dto.Releases is null ? null : MapReleaseSource(dto.Releases),
            gameMax: dto.Compatibility.GameMax,
            os: RequireItems(dto.Compatibility.Os, "compatibility.os"),
            loader: dto.Loader is null ? null : MapLoaderRequirement(dto.Loader),
            dependencies: MapItems(dto.Dependencies, MapDependency, "dependencies")!,
            install: dto.Install is null ? null : MapInstallDescriptor(dto.Install),
            provides: dto.Provides is null ? null : MapProvides(dto.Provides)));
    }

    // Requires the authored ModMetadata to make sure listing has all the correct info
    public static ModVersionMetadata MapRelease(ReleasesEntryDto dto, string? source, ModMetadata? authored)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return MapInput(() => new ModVersionMetadata(
            specVersion: dto.SpecVersion,
            modId: dto.Id,
            version: ModVersion.Parse(dto.Version),
            releaseStatus: MapReleaseStatus(dto.ReleaseStatus),
            releaseDate: DateTimeOffset.Parse(dto.ReleaseDate, System.Globalization.CultureInfo.InvariantCulture),
            gameMin: dto.GameMin,
            gameMinRevision: dto.GameMinRevision,
            download: MapDownloadInfo(dto.Download),
            installSizeBytes: dto.InstallSize,
            dependencies: MapItems(dto.Dependencies, MapDependency, "dependencies")!,
            type: MapContentType(dto.Type),
            versionScheme: dto.VersionScheme,
            gameMax: dto.GameMax,
            gameMaxRevision: dto.GameMaxRevision,
            os: RequireItems(dto.Os, "os"),
            install: dto.Install is null ? null : MapInstallInfo(dto.Install),
            loader: dto.Loader is null ? null : MapLoaderRequirement(dto.Loader),
            changelog: dto.Changelog,
            // Absent "listing" key entirely -> no snapshot to merge, leave null.
            // Present but incomplete -> merge with the live authored data.
            listing: dto.Listing is { } listingElement ? MapListingSnapshot(listingElement, authored) : null,
            yanked: dto.Yanked ?? false,
            yankedReason: dto.YankedReason,
            source: source));
    }

    public static ModPackMetadata MapPackVersion(PackAuthoredDto dto, string source)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return MapInput(() => new ModPackMetadata(
            specVersion: dto.SpecVersion,
            modPackId: dto.Id,
            source: source,
            name: dto.Name,
            authors: RequireItems(dto.Authors, "authors")!,
            abstractText: dto.Abstract,
            license: dto.License,
            links: MapLinks(dto.Links),
            gameMin: dto.Compatibility.GameMin,
            version: ModVersion.Parse(dto.Version),
            releasedAt: DateTimeOffset.Parse(dto.ReleasedAt, System.Globalization.CultureInfo.InvariantCulture),
            mods: MapItems(dto.Mods, MapPackEntry, "mods")!,
            tags: RequireItems(dto.Tags, "tags"),
            description: dto.Description,
            status: MapModStatus(dto.Status),
            supersededBy: dto.SupersededBy,
            gameMax: dto.Compatibility.GameMax,
            os: RequireItems(dto.Compatibility.Os, "compatibility.os"),
            changelog: dto.ChangeLog,
            vehicles: MapItems(dto.Vehicles, MapPackEntry, "vehicles"),
            saves: MapItems(dto.Saves, MapPackEntry, "saves")));
    }

    public static IndexStatus MapIndexStatus(IndexStatusDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return MapInput(() => new IndexStatus(MapIndexStatusState(dto.State), dto.State, dto.Since, dto.Reason));
    }

    // Enum / value mappings

    private static ModStatus MapModStatus(string? status) =>
        status is null ? ModStatus.Active : MetadataEnumMapper.ParseModStatus(status);

    private static ReleaseStatus MapReleaseStatus(string releaseStatus) => MetadataEnumMapper.ParseReleaseStatus(releaseStatus);

    private static ContentType MapContentType(string type) => MetadataEnumMapper.ParseContentType(type);

    private static ModDependencyKind MapDependencyKind(string kind) => MetadataEnumMapper.ParseKind(kind);

    private static IndexStatusState MapIndexStatusState(string state) => state switch
    {
        "delisted" => IndexStatusState.Delisted,
        "disputed" => IndexStatusState.Disputed,
        "retracted" => IndexStatusState.Retracted,
        _ => IndexStatusState.Unknown,
    };

    private static InstallAnchor MapInstallAnchor(string target) => MetadataEnumMapper.ParseAnchor(target);

    private static MetadataSource MapMetadataSource(string source) => MetadataEnumMapper.ParseSource(source)!.Value;

    private static ConfigureFormat MapConfigureFormat(string format) => MetadataEnumMapper.ParseConfigureFormat(format);

    // Object mappings

    private static ModPackEntry MapPackEntry(IndexModPackItemEntryDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new ModPackEntry(dto.Id, ModVersion.Parse(dto.Version));
    }

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
                if (string.Equals(key, "forums", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"Link key '{key}' collides with the declared forums link.");

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
        ArgumentNullException.ThrowIfNull(dto);

        var kind = MapDependencyKind(dto.Kind);
        var source = dto.Source is null ? null : (MetadataSource?)MapMetadataSource(dto.Source);

        if (dto.Id is not null && dto.AnyOf is not null)
            throw new FormatException("A dependency cannot declare both id and any_of.");

        if (dto.Id is null && dto.AnyOf is null)
            throw new FormatException("A dependency must declare id or any_of.");

        if (dto.AnyOf is not null)
        {
            if (dto.AnyOf.Count == 0)
                throw new FormatException("A dependency any_of list cannot be empty.");

            if (dto.Min is not null || dto.Max is not null)
                throw new FormatException("A dependency with any_of must put version bounds on each alternative.");

            var alternatives = MapItems(
                dto.AnyOf,
                alternative => new ModDependencyAlternative(
                    alternative.Id,
                    MetadataEnumMapper.ParseVersion(alternative.Min),
                    MetadataEnumMapper.ParseVersion(alternative.Max)),
                "dependency.any_of")!;

            return ModDependency.OfAlternatives(kind, alternatives, source);
        }

        return new ModDependency(
            dto.Id!,
            kind,
            MetadataEnumMapper.ParseVersion(dto.Min),
            MetadataEnumMapper.ParseVersion(dto.Max),
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
            manages: RequireItems(dto.Manages, "install.manages"),
            steps: RequireItems(dto.Steps, "install.steps"),
            uninstall: RequireItems(dto.Uninstall, "install.uninstall"));

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

    private static LoaderConfigure MapConfigure(ConfigureDto dto)
    {
        if (dto.UnknownFields is { Count: > 0 })
            throw new FormatException($"The loader configure table has unknown member '{dto.UnknownFields.Keys.First()}'.");

        return new LoaderConfigure(dto.File, MapConfigureFormat(dto.Format), dto.GamePath);
    }

    private static DownloadInfo MapDownloadInfo(DownloadInfoDto dto) =>
        new(dto.URL, dto.SHA256, dto.Size, dto.ContentType, RequireItems(dto.Mirrors, "download.mirrors"));

    /// <summary>
    /// Builds the release-time listing snapshot by merging whatever the
    /// release's own "listing" JSON provides with the live authored metadata,
    /// field by field. Only a missing field falls back to the authored value.
    /// </summary>
    private static ListingSnapshot? MapListingSnapshot(JsonElement element, ModMetadata? authored)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException("The release listing field must be an object.");

        RequirePropertyKind(element, "name", JsonValueKind.String);
        RequirePropertyKind(element, "authors", JsonValueKind.Array);
        RequirePropertyKind(element, "abstract", JsonValueKind.String);
        RequirePropertyKind(element, "description", JsonValueKind.String);
        RequirePropertyKind(element, "license", JsonValueKind.String);
        RequirePropertyKind(element, "tags", JsonValueKind.Array);
        RequirePropertyKind(element, "links", JsonValueKind.Object);

        var dto = element.Deserialize<ReleaseListingDto>(IndexJsonOptions.Value)
            ?? throw new JsonException("The release listing deserialized to null.");

        if (authored is null)
        {
            if (dto.Name is null || dto.Authors is null ||
                dto.Abstract is null || dto.License is null)
            {
                throw new FormatException("The release listing is incomplete and no authored listing is available for fallback.");
            }

            return new ListingSnapshot(
                dto.Name,
                RequireItems(dto.Authors, "listing.authors")!,
                dto.Abstract,
                dto.License,
                RequireItems(dto.Tags, "listing.tags"),
                dto.Links is null ? null : MapLinks(dto.Links),
                dto.Description);
        }

        return new ListingSnapshot(
            dto.Name ?? authored.Name,
            dto.Authors is null ? authored.Authors : RequireItems(dto.Authors, "listing.authors")!,
            dto.Abstract ?? authored.Abstract,
            dto.License ?? authored.License,
            dto.Tags is null ? authored.Tags : RequireItems(dto.Tags, "listing.tags"),
            dto.Links is null ? authored.Links : MapLinks(dto.Links),
            dto.Description ?? authored.Description);
    }

    private static IReadOnlyList<T>? RequireItems<T>(IReadOnlyList<T>? items, string field)
    {
        if (items is null)
            return null;

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is null)
                throw new FormatException($"The {field} collection has a null element at index {index}.");
        }

        return items;
    }

    private static IReadOnlyList<TResult>? MapItems<TSource, TResult>(
        IReadOnlyList<TSource>? items,
        Func<TSource, TResult> map,
        string field)
    {
        if (items is null)
            return null;

        RequireItems(items, field);
        return items.Select(map).ToList();
    }

    private static void RequirePropertyKind(JsonElement element, string propertyName, JsonValueKind expectedKind)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind != expectedKind)
        {
            throw new FormatException(
                $"The release listing field '{propertyName}' must be {expectedKind}, but was {property.ValueKind}.");
        }
    }

    private static TResult MapInput<TResult>(Func<TResult> map)
    {
        try
        {
            return map();
        }
        catch (ArgumentException ex)
        {
            throw new IndexInputException(ex.Message, ex);
        }
    }
}
