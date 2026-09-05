using Borea.Core.Settings;

namespace Borea.Storage.Settings;

public static class BoreaSettingsMapper
{
    public static BoreaSettingsDto ToDto(BoreaSettings settings) => new()
    {
        GameDirectoryPath = settings.GameDirectoryPath,
        LoaderDirectoryPaths = settings.LoaderDirectoryPaths.Count == 0
            ? null
            : settings.LoaderDirectoryPaths.ToDictionary(p => p.Key, p => p.Value),
    };

    public static BoreaSettings FromDto(BoreaSettingsDto dto) =>
        new(dto.GameDirectoryPath, dto.LoaderDirectoryPaths);
}
