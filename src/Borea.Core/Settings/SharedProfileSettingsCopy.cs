namespace Borea.Core.Settings;

public enum SharedProfileSettingsCopyOutcome
{
    /// <summary>The game's settings.toml now holds the settings of the instance.</summary>
    Copied = 0,

    /// <summary>A KSA or mod loader process runs, and the game writes its settings.toml when it closes.</summary>
    GameRunning = 1,

    /// <summary>The instance has no settings.toml, because the game did not start in it yet.</summary>
    NoInstanceSettings = 2,
}

/// <param name="Outcome">Whether the copy was made, or why not.</param>
/// <param name="BackupPath">The copy of the game's old settings.toml. Null when nothing was copied or the game had no settings.toml before.</param>
public sealed record SharedProfileSettingsCopy(SharedProfileSettingsCopyOutcome Outcome, string? BackupPath = null);
