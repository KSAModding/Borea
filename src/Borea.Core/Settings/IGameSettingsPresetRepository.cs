using Borea.Core.Game;

namespace Borea.Core.Settings;

/// <summary>
/// The saved game settings presets. A preset is a folder under the Borea root
/// with the copied <c>settings.toml</c> and a record of its name and version.
/// </summary>
public interface IGameSettingsPresetRepository
{
    /// <summary>
    /// Every saved preset. A folder that does not read as one is skipped.
    /// </summary>
    Task<IReadOnlyList<GameSettingsPreset>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The preset with that id, or null when there is none.
    /// </summary>
    Task<GameSettingsPreset?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the preset's <c>settings.toml</c> into the instance, replacing the file it has.
    /// </summary>
    Task ApplyAsync(Guid presetId, Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the given settings file into a new preset. The file must parse as TOML.
    /// </summary>
    Task<GameSettingsPreset> SaveAsync(string name, GameVersion gameVersion, string sourceSettingsTomlPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the preset and its files. A preset that is not there is not an error.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
