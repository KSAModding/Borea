namespace Borea.Core.State;

/// <summary>
/// Tracks which mods are currently active within a specific instance.
/// </summary>
public interface IModStateRepository
{
    /// <summary>
    /// Returns true if the given mod is currently active within the instance, else false
    /// </summary>
    Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all currently active mods within the instance.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the given mod within the instance. No-op if the mod is already active.
    /// </summary>
    Task SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the given mod within the instance. No-op if the mod is
    /// not currently active.
    /// </summary>
    Task SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default);
}