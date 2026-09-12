using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.Storage.Settings;

public static class BoreaSettingsMapper
{
    public static BoreaSettingsDto ToDto(BoreaSettings settings) => new()
    {
        GameDirectoryPath = settings.GameDirectoryPath,
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
            pair => FromDto(pair.Value)));

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
