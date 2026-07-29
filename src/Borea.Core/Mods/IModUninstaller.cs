namespace Borea.Core.Mods;

/// <summary>
/// Removes an installed mod's files from a specific instance.
/// </summary>
public interface IModUninstaller
{
    /// <summary>
    /// Uninstalls a mod from the specified instance by removing its files. Does not check for dependencies.
    /// No-op if the mod does not exist.
    /// </summary>
    /// <param name="instanceId">The ID of the instance to remove the mod from</param>
    /// <param name="modId">The ID of the mod to uninstall</param>
    Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);
}