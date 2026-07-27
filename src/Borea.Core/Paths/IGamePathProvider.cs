namespace Borea.Core.Paths;

/// <summary>
/// Resolves filesystem locations relevant to KSA, StarMap, and Borea's own
/// library.
/// </summary>
public interface IGamePathProvider
{
    /// <summary>
    /// Root of all Borea instance storage
    /// </summary>
    string GetInstancesRoot();

    /// <summary>
    /// Path to the file tracking which instance is currently selected for
    /// launch.
    /// </summary>
    string GetActiveInstancePointerPath();

    /// <summary>
    /// Root folder for a specific instance, e.g.
    /// </summary>
    string GetInstanceRoot(Guid instanceId);

    string GetInstanceModsFolder(Guid instanceId);

    string GetInstanceSavesFolder(Guid instanceId);

    string GetInstanceVehiclesFolder(Guid instanceId);

    string GetInstanceSettingsPath(Guid instanceId);

    string GetInstanceManifestPath(Guid instanceId);

    string GetInstanceMetadataPath(Guid instanceId);

    /// <summary>
    /// Root directory of the KSA game installation itself (not the Documents
    /// folder).
    /// </summary>
    string GetGameDirectoryPath();

    /// <summary>
    /// Root directory of the StarMap installation. Used as the base
    /// path for launching StarMap.
    /// </summary>
    string GetStarMapDirectoryPath();
}