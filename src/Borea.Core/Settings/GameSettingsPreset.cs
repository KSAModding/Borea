using Borea.Core.Game;

namespace Borea.Core.Settings;

public class GameSettingsPreset
{
    public Guid Id { get; }
    public string Name { get; }
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
