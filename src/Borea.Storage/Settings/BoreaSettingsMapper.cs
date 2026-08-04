using Borea.Core.Settings;

namespace Borea.Storage.Settings;

public static class BoreaSettingsMapper
{
    public static BoreaSettingsDto ToDto(BoreaSettings settings) => new()
    {
        GameDirectoryPath = settings.GameDirectoryPath,
        StarMapDirectoryPath = settings.StarMapDirectoryPath,
    };

    public static BoreaSettings FromDto(BoreaSettingsDto dto) =>
        new(dto.GameDirectoryPath, dto.StarMapDirectoryPath);
}
