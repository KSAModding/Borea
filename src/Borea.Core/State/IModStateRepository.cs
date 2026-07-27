using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Borea.Core.State;

/// <summary>
/// Tracks which version of each mod is currently active within a specific
/// instance.
/// </summary>
public interface IModStateRepository
{
    /// <summary>
    /// Returns the currently active version of the given mod within the
    /// instance, or null if the mod has no active version (not installed,
    /// or explicitly inactive).
    /// </summary>
    Task<Mods.ModVersion?> GetActiveVersionAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all currently active mods within the instance as a
    /// ModId -> ModVersion mapping.
    /// </summary>
    Task<IReadOnlyDictionary<string, Mods.ModVersion>> GetAllActiveAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the given mod version within the instance, replacing any
    /// previously active version of the same mod.
    /// </summary>
    Task SetActiveAsync(Guid instanceId, string modId, Mods.ModVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the given mod within the instance. No-op if the mod is
    /// not currently active.
    /// </summary>
    Task SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);
}