using Borea.Core.Mods;

namespace Borea.Core.State;

/// <summary>
/// Tracks which version of each mod is currently active — i.e. reflected in
/// KSA's manifest.toml and symlinked into the mods folder.
/// </summary>
public interface IModStateRepository
{
    /// <summary>
    /// Returns the currently active version of the given mod, or null if
    /// the mod has no active version (not installed, or explicitly inactive).
    /// </summary>
    Task<Mods.ModVersion?> GetActiveVersionAsync(string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all currently active mods as a ModId -> ModVersion mapping.
    /// </summary>
    Task<IReadOnlyDictionary<string, Mods.ModVersion>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the given mod version as active, replacing any previously
    /// active version of the same mod.
    /// </summary>
    Task SetActiveAsync(string modId, Mods.ModVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the given mod as having no active version.
    /// </summary>
    Task SetInactiveAsync(string modId, CancellationToken cancellationToken = default);
}