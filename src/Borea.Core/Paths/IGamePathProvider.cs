using Borea.Core.Game;
using Borea.Core.Mods;

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
    /// Path to the cached announcements of the KSAModding team.
    /// </summary>
    string GetAnnouncementsPath();

    /// <summary>
    /// Path to the cached authored schema of content-index, which the listing checks use.
    /// </summary>
    string GetListingSchemaPath();

    /// <summary>
    /// Folder of the listing images Borea verified, one file per SHA-256.
    /// </summary>
    string GetImageCacheFolder();

    /// <summary>
    /// Folder of the game patch notes files Borea downloaded, under their published names.
    /// </summary>
    string GetGamePatchNotesFolder();

    /// <summary>
    /// Folder of the vehicle preview cache, with the archives it fetched before an install and its still images.
    /// The folder name holds the format version, so a Borea with another format never reads these files.
    /// </summary>
    string GetPreviewCacheFolder();

    string GetInstancesRoot();

    /// <summary>
    /// Root of the mod releases that instances share, one folder per release below the folder of its mod.
    /// </summary>
    string GetStaticModFilesRoot();

    /// <summary>
    /// The Game Builds folder of the library, which holds one folder per kept game build.
    /// </summary>
    string GetGameBuildsRoot();

    /// <summary>
    /// Folder of one kept game build, below the Game Builds folder of the library.
    /// Only Year.Month.BuildNumber.Revision name the folder, without the suffix, so two builds that differ only by their suffix share one folder.
    /// </summary>
    string GetGameBuildFolder(GameVersion version);

    /// <summary>
    /// Root for loaders installed standalone (RFC 0035). Each loader has a folder here, and it holds a folder for each installed version.
    /// </summary>
    string GetLoadersRoot();

    /// <summary>
    /// The folder of one version of a loader, Loaders/&lt;id&gt;/&lt;version&gt;. It does not say whether that version is installed.
    /// </summary>
    string GetLoaderVersionDirectoryPath(string loaderId, ModVersion version);

    /// <summary>
    /// Root for all presets in Borea.
    /// </summary>
    string GetPresetsRoot();

    /// <summary>
    /// Root directory for the KSA game settings presets.
    /// </summary>
    string GetGameSettingsPresetsRoot();

    /// <summary>
    /// Path to the file tracking which instance is currently selected for
    /// launch.
    /// </summary>
    string GetActiveInstancePointerPath();

    string GetModFavoritesPath();

    string GetModPackFavoritesPath();

    /// <summary>
    /// Path to the record of what the player shared on a share host, one file for the whole computer.
    /// </summary>
    string GetSharedContentPath();

    /// <summary>
    /// Path to Borea's own settings.toml (game/StarMap install locations).
    /// </summary>
    string GetBoreaSettingsPath();

    /// <summary>
    /// Path to Borea.App preferences and custom themes.
    /// </summary>
    string GetAppPreferencesPath();

    /// <summary>
    /// Path to the history of the tasks Borea.App ran.
    /// </summary>
    string GetTaskHistoryPath();

    /// <summary>
    /// Lock file that the running Borea.App holds open, so that only one App runs for each Borea root.
    /// </summary>
    string GetAppLockPath();

    /// <summary>
    /// Folder of the secrets that the secret store of the system protects, where that store needs a file, as DPAPI on Windows does.
    /// </summary>
    string GetSecretsFolder();

    /// <summary>
    /// Folder of Borea's daily log files.
    /// </summary>
    string GetLogsFolder();

    /// <summary>
    /// Folder of the translation files that the App reads at start, so a translator sees a translation without a build.
    /// </summary>
    string GetLanguagesFolder();

    /// <summary>
    /// Root of the backups of saves and vehicles, one folder per instance.
    /// </summary>
    string GetBackupsRoot();

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

    /// <summary>
    /// The sessions Borea read from the instance's archived game logs.
    /// </summary>
    string GetInstancePlaytimePath(Guid instanceId);

    /// <summary>
    /// The vehicles and saves Borea installed in the instance, with a hash of each file.
    /// A file of its own, so a Borea that does not know it keeps every record when it writes instance.toml.
    /// </summary>
    string GetInstanceInstalledContentPath(Guid instanceId);

    string GetInstanceManifestPath(Guid instanceId);

    string GetInstanceMetadataPath(Guid instanceId);

    /// <summary>
    /// Root directory of the KSA game installation itself (not the Documents
    /// folder).
    /// </summary>
    /// <returns>Null when the path is unknown.</returns>
    string? GetGameDirectoryPath();

    /// <summary>
    /// The profile the game uses when it starts without a loader, Constants.DocumentsFolderPath.
    /// </summary>
    string GetSharedProfileRoot();

    /// <summary>
    /// The folder of the installation that the settings record for a loader, which an older Borea starts. Other
    /// versions of the loader can be installed beside it, and an instance can start any of them. Ids compare
    /// case-insensitively, and no id at all throws.
    /// </summary>
    /// <returns>Null when no path is known for that loader.</returns>
    string? GetLoaderDirectoryPath(string loaderId);
}
