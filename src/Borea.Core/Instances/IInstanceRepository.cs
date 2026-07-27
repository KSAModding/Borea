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
    /// Whether <paramref name="name"/> is free to use. Pass
    /// <paramref name="excludingInstanceId"/> when checking availability for
    /// a rename, so an instance's current name doesn't count as "taken" by
    /// itself.
    /// </summary>
    Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null);

    Task<Instance> CreateAsync(string name, InstanceSource source);

    Task RenameAsync(Guid instanceId, string newName);

    Task DeleteAsync(Guid instanceId);

    Task SaveAsync(Instance instance);
}