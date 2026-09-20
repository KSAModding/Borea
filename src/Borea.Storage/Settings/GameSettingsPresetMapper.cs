using Borea.Core.Game;
using Borea.Core.Settings;

namespace Borea.Storage.Settings;

public static class GameSettingsPresetMapper
{
    public static GameSettingsPresetDto ToDto(GameSettingsPreset preset) => new()
    {
        Id = preset.Id.ToString(),
        Name = preset.Name,
        Version = preset.Version.ToString(),
    };

    public static GameSettingsPreset? FromDto(GameSettingsPresetDto dto)
    {
        if (!Guid.TryParse(dto.Id, out var id))
            return null;

        if (!GameVersion.TryParse(dto.Version, out var version))
            return null;

        return new GameSettingsPreset(id, dto.Name, version);
    }
}
