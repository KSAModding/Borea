using Borea.Core.Game;

namespace Borea.Core.Settings;

/// <summary>
/// A copy of an instance's <c>settings.toml</c>, kept under its own id, that a
/// new instance can start from instead of the settings the game writes fresh.
/// </summary>
public sealed class GameSettingsPreset
{
    public Guid Id { get; }

    /// <summary>The name the player gave it.</summary>
    public string Name { get; }

    /// <summary>The installed game version when the preset was saved, because the game rewrites its settings between builds.</summary>
    public GameVersion Version { get; }

    public GameSettingsPreset(Guid id, string name, GameVersion version)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Settings preset name cannot be null or whitespace.", nameof(name));

        Id = id;
        Name = name;
        Version = version;
    }
}
