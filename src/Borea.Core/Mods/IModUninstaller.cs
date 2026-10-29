namespace Borea.Core.Mods;

/// <summary>
/// Removes a mod Borea installed from a specific instance: its folder and its
/// record on the instance.
/// </summary>
public interface IModUninstaller
{
    /// <summary>
    /// Deletes the folder only when the installed record says Borea owns it,
    /// then removes the record, while other changes to the instance wait. A
    /// missing record or a foreign record leaves the folder and the instance
    /// unchanged. A Borea record without a verifiable ownership marker causes
    /// an error and changes nothing. The manifest entry stays, because the
    /// game removes an entry that names no folder on its next launch.
    /// </summary>
    Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);
}
