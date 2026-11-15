namespace Borea.Core.Settings;

/// <summary>The settings.toml of the game without mods, in the game's own profile.</summary>
public interface ISharedProfileSettings
{
    /// <summary>Whether the game has a settings.toml, which a new instance then starts with.</summary>
    bool Exists();

    /// <summary>The folder where a copy from an instance keeps the settings.toml it replaced.</summary>
    string BackupFolder { get; }

    /// <summary>
    /// Replaces the game's settings.toml with the settings.toml of the instance, after it keeps the old file in
    /// <see cref="BackupFolder"/>. Nothing else in the game's profile changes. It is refused while the game runs and
    /// when the instance has no settings.toml.
    /// </summary>
    /// <exception cref="InvalidOperationException">The settings.toml of the instance is not valid TOML.</exception>
    Task<SharedProfileSettingsCopy> CopyFromInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
