namespace Borea.Core.Paths;

/// <summary>
/// Resolves filesystem locations relevant to KSA, StarMap, and Borea's own
/// library.
/// </summary>
public interface IGamePathProvider
{
    /// <summary>
    /// Path to the cached content index snapshot
    /// </summary>
    string GetIndexPath();

    /// <summary>
    /// Root of all Borea instance storage
    /// </summary>
    string GetInstancesRoot();

    /// <summary>
    /// Root for loaders installed standalone (RFC 0035), one folder per loader id.
    /// </summary>
    string GetLoadersRoot();

    /// <summary>
    /// Path to the file tracking which instance is currently selected for
    /// launch.
    /// </summary>
    string GetActiveInstancePointerPath();

    /// <summary>
    /// Path to the file tracking the user's favorited/bookmarked mods.
    /// </summary>
    string GetModFavoritesPath();

    /// <summary>
    /// Path to the file tracking the user's favorited/bookmarked mod packs.
    /// </summary>
    string GetModPackFavoritesPath();

    /// <summary>
    /// Path to Borea's own settings.toml (game/StarMap install locations).
    /// </summary>
    string GetBoreaSettingsPath();

    /// <summary>
    /// Path to Borea.App preferences and custom themes.
    /// </summary>
    string GetAppPreferencesPath();

    /// <summary>
    /// Folder of Borea's daily log files.
    /// </summary>
    string GetLogsFolder();

    /// <summary>
    /// Root folder for a specific instance, e.g.
    /// </summary>
    string GetInstanceRoot(Guid instanceId);

    string GetInstanceModsFolder(Guid instanceId);

    string GetInstanceSavesFolder(Guid instanceId);

    string GetInstanceVehiclesFolder(Guid instanceId);

    string GetInstanceSettingsPath(Guid instanceId);

    string GetInstanceHudLayoutsFolder(Guid instanceId);

    string GetInstanceCrashDumpsFolder(Guid instanceId);

    string GetInstanceExportsFolder(Guid instanceId);

    /// <summary>
    /// The log the game writes in the instance, Constants.LogsFolderPath below the instance root.
    /// </summary>
    string GetInstanceGameLogPath(Guid instanceId);

    /// <summary>
    /// What the mod loader wrote while Borea watched its start, next to the game log.
    /// </summary>
    string GetInstanceLaunchLogPath(Guid instanceId);

    string GetInstanceManifestPath(Guid instanceId);

    string GetInstanceMetadataPath(Guid instanceId);

    /// <summary>
    /// Root directory of the KSA game installation itself (not the Documents
    /// folder).
    /// </summary>
    /// <returns><list type="bullet">
    /// <item>null if path is unknown.</item>
    /// <item>string path if known.</item>
    /// </list></returns>
    string? GetGameDirectoryPath();

    /// <summary>
    /// Root directory of one installed mod loader. Ids compare
    /// case-insensitively, and no id at all throws.
    /// </summary>
    /// <returns><list type="bullet">
    /// <item>null if no path is known for that loader.</item>
    /// <item>string path if known.</item>
    /// </list></returns>
    string? GetLoaderDirectoryPath(string loaderId);
}
