namespace Borea.Core.Instances;

/// <summary>
/// Owns the identity, naming, and lifecycle of instances.
/// </summary>
public interface IInstanceRepository
{
    Task<IReadOnlyList<Instance>> GetAllAsync();

    /// <summary>
    /// The instance with the given ID, or null if none exists.
    /// </summary>
    Task<Instance?> GetByIdAsync(Guid instanceId);

    Task<Guid?> GetActiveInstanceIdAsync();

    Task SetActiveInstanceAsync(Guid instanceId);

    /// <summary>
    /// Leaves no instance active. Does nothing when none is active.
    /// </summary>
    Task ClearActiveInstanceAsync();

    /// <summary>
    /// Whether <paramref name="name"/> is free to use. Pass
    /// <paramref name="excludingInstanceId"/> when checking availability for
    /// a rename, so an instance's current name doesn't count as "taken" by
    /// itself.
    /// </summary>
    Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null);

    /// <summary>
    /// Saves a new instance, and makes it active when no existing instance is active.
    /// </summary>
    Task<InstanceCreateResult> CreateAsync(string name, InstanceSource source);

    /// <summary>
    /// Saves <paramref name="instance"/> as a new instance under its own id, with the same rule.
    /// </summary>
    Task<InstanceCreateResult> CreateAsync(Instance instance);

    Task RenameAsync(Guid instanceId, string newName);

    /// <summary>
    /// Deletes the instance, and leaves no instance active when it was the active one.
    /// </summary>
    Task DeleteAsync(Guid instanceId);

    Task SaveAsync(Instance instance);

    /// <summary>
    /// Loads, changes, and saves one instance while other changes to that
    /// instance wait. The change is not saved if <paramref name="update"/>
    /// throws.
    /// </summary>
    Task<TResult> UpdateAsync<TResult>(
        Guid instanceId,
        Func<Instance, TResult> update,
        CancellationToken cancellationToken = default);
}
