using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class ModVersionMetadataMapper
{
    public static ModVersionMetadataDto ToDto(ModVersionMetadata release) => new()
    {
        SpecVersion = release.SpecVersion,
        ModId = release.ModId,
        Type = MetadataEnumMapper.ToDto(release.Type),
        Version = release.Version.ToString(),
        VersionScheme = release.VersionScheme,
        ReleaseStatus = MetadataEnumMapper.ToDto(release.ReleaseStatus),
        ReleaseDate = release.ReleaseDate,
        GameMin = release.GameMin,
        GameMinRevision = release.GameMinRevision,
        GameMax = release.GameMax,
        GameMaxRevision = release.GameMaxRevision,
        Os = release.Os?.ToList(),
        Download = DownloadInfoMapper.ToDto(release.Download),
        InstallSizeBytes = release.InstallSizeBytes,
        Install = release.Install is null ? null : InstallInfoMapper.ToDto(release.Install),
        Loader = release.Loader is null ? null : LoaderRequirementMapper.ToDto(release.Loader),
        Dependencies = release.Dependencies.Select(ModDependencyMapper.ToDto).ToList(),
        Changelog = release.Changelog,
        Listing = release.Listing is null ? null : ListingSnapshotMapper.ToDto(release.Listing),
        Yanked = release.Yanked,
        YankedReason = release.YankedReason,
        Source = release.Source,
    };

    public static ModVersionMetadata FromDto(ModVersionMetadataDto dto)
    {
        if (dto.SpecVersion < 1)
            throw new FormatException(dto.SpecVersion == 0
                ? "The release file predates the metadata model (no spec version)."
                : $"The release file carries an invalid spec version ({dto.SpecVersion}).");

        return new ModVersionMetadata(
        specVersion: dto.SpecVersion,
        modId: dto.ModId,
        version: ModVersion.Parse(dto.Version),
        releaseStatus: MetadataEnumMapper.ParseReleaseStatus(dto.ReleaseStatus),
        releaseDate: dto.ReleaseDate,
        gameMin: dto.GameMin,
        gameMinRevision: dto.GameMinRevision,
        download: DownloadInfoMapper.FromDto(dto.Download),
        installSizeBytes: dto.InstallSizeBytes,
        dependencies: dto.Dependencies.Select(ModDependencyMapper.FromDto).ToList(),
        type: MetadataEnumMapper.ParseContentType(dto.Type),
        versionScheme: dto.VersionScheme,
        gameMax: dto.GameMax,
        gameMaxRevision: dto.GameMaxRevision,
        os: dto.Os,
        install: dto.Install is null ? null : InstallInfoMapper.FromDto(dto.Install),
        loader: dto.Loader is null ? null : LoaderRequirementMapper.FromDto(dto.Loader),
        changelog: dto.Changelog,
        listing: dto.Listing is null ? null : ListingSnapshotMapper.FromDto(dto.Listing),
        yanked: dto.Yanked,
        yankedReason: dto.YankedReason,
        source: dto.Source);
    }
}
