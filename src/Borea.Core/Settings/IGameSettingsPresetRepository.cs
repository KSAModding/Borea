using Borea.Core.Game;

namespace Borea.Core.Settings;

public interface IGameSettingsPresetRepository
{
    /// <summary>
    /// Lists all the known <see cref="GameSettingsPreset"/>
    /// </summary>
    Task<IReadOnlyList<GameSettingsPreset>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the <see cref="GameSettingsPreset"/> by its <see cref="Guid"/>
    /// </summary>
    Task<GameSettingsPreset?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 
    /// </summary>
    Task ApplyAsync(Guid presetId, Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new <see cref="GameSettingsPreset"/> to the specified path
    /// </summary>
    Task<GameSettingsPreset> SaveAsync(string name, GameVersion gameVersion, string sourceSettingsTomlPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a <see cref="GameSettingsPreset"/> using its <see cref="Guid"/>
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
