using Borea.Core.Mods;

namespace Borea.Core.State;

/// <summary>
/// Performs mod activation and deactivation against KSA's manifest.toml and
/// the mods folder symlinks, in the order required to avoid triggering
/// KSA's "unregistered folder" startup prompt:
///   Activate:   write manifest entry, then create symlink.
///   Deactivate: remove symlink, then remove manifest entry.
/// </summary>
public interface IModActivationService
{
    /// <summary>
    /// Activates the given mod version: writes its manifest entry, then
    /// creates the symlink into the mods folder. If another version of the
    /// same mod is currently active, it is deactivated first.
    /// </summary>
    Task ActivateAsync(string modId, Mods.ModVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the given mod: removes its symlink from the mods folder,
    /// then removes its manifest entry. No-op if the mod is not active.
    /// </summary>
    Task DeactivateAsync(string modId, CancellationToken cancellationToken = default);
}