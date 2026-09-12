namespace Borea.Core.Mods;

/// <summary>
/// Removes Borea-owned mod files from a specific instance.
/// </summary>
public interface IModUninstaller
{
    /// <summary>
    /// Removes files only when the installed record says Borea owns them.
    /// A missing record or a foreign record leaves the folder unchanged. A
    /// Borea record without a verifiable ownership marker causes an error.
    /// </summary>
    /// <param name="instanceId">The ID of the instance to remove the mod from</param>
    /// <param name="modId">The ID of the mod to uninstall</param>
    Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);
}
