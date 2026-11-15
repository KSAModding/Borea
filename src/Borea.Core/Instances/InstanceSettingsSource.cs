namespace Borea.Core.Instances;

/// <summary>
/// Where the settings.toml of a new instance comes from. Borea copies the file once, when it creates the instance,
/// so a later change in the instance or in the source does not reach the other.
/// </summary>
public abstract record InstanceSettingsSource
{
    private InstanceSettingsSource()
    {
    }

    /// <summary>The settings.toml of the game without mods. When the game has none, the instance starts without one and the game writes its defaults.</summary>
    public sealed record FromGame : InstanceSettingsSource
    {
        public static readonly FromGame Value = new();

        private FromGame()
        {
        }
    }

    /// <summary>No settings.toml, so the game writes its defaults on the first start.</summary>
    public sealed record GameDefaults : InstanceSettingsSource
    {
        public static readonly GameDefaults Value = new();

        private GameDefaults()
        {
        }
    }

    public sealed record FromPreset(Guid PresetId) : InstanceSettingsSource;

    /// <summary>The settings.toml of another instance, or the one of the game when that instance has none.</summary>
    public sealed record FromInstance(Guid InstanceId) : InstanceSettingsSource;
}
