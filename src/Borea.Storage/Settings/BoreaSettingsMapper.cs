using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.Storage.Settings;

public static class BoreaSettingsMapper
{
    public static BoreaSettingsDto ToDto(BoreaSettings settings) => new()
    {
        GameDirectoryPath = settings.GameDirectoryPath,
        ReleaseChannel = settings.ReleaseChannel.ToName(),
        LibraryFolderPath = settings.LibraryFolderPath,
        LoaderInstallations = settings.LoaderInstallations.Count == 0
            ? null
            : settings.LoaderInstallations.ToDictionary(
                pair => pair.Key,
                pair => ToDto(pair.Value)),
    };

    public static BoreaSettings FromDto(BoreaSettingsDto dto) => new(
        dto.GameDirectoryPath,
        dto.LoaderInstallations?.ToDictionary(
            pair => pair.Key,
            pair => FromDto(pair.Value)),
        ReadReleaseChannel(dto.ReleaseChannel),
        dto.LibraryFolderPath);

    /// <summary>An absent or unknown name loads as stable, which offers the fewest releases.</summary>
    private static ReleaseChannel ReadReleaseChannel(string? name)
        => ReleaseChannels.TryParse(name, out var channel) ? channel : ReleaseChannel.Stable;

    private static LoaderInstallationDto ToDto(LoaderInstallation installation) => new()
    {
        DirectoryPath = installation.DirectoryPath,
        Version = installation.Version?.ToString(),
        RawVersion = installation.RawVersion,
        IsAdopted = installation.IsAdopted,
    };

    private static LoaderInstallation FromDto(LoaderInstallationDto dto) => new(
        dto.DirectoryPath,
        dto.Version is null ? null : ModVersion.Parse(dto.Version),
        dto.RawVersion,
        dto.IsAdopted);
}
