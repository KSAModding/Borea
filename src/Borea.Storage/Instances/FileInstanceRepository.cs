using Borea.Core.Instances;
using Borea.Core.Paths;
using Borea.Storage.Toml;

namespace Borea.Storage.Instances;

/// <summary>
/// File-backed implementation of IInstanceRepository. Each instance's
/// metadata (and its full InstalledMods list, per the earlier one-file
/// decision) lives at IGamePathProvider.GetInstanceMetadataPath(instanceId).
/// Instance existence is derived by enumerating directories under
/// GetInstancesRoot(), since InstanceId is also the folder name.
/// </summary>
public sealed class FileInstanceRepository : IInstanceRepository
{
    private readonly IGamePathProvider _pathProvider;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> _instanceLocks = new();

    public FileInstanceRepository(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }


    /// <summary>
    /// Returns all instances that exist on disk. An instance whose file cannot
    /// be read or mapped is skipped, so one broken file does not take the whole
    /// list down; loading it directly by id still surfaces the error.
    /// </summary>
    public async Task<IReadOnlyList<Instance>> GetAllAsync()
    {
        var root = _pathProvider.GetInstancesRoot();
        if (!Directory.Exists(root))
            return Array.Empty<Instance>();

        var instances = new List<Instance>();

        foreach (var dir in Directory.GetDirectories(root))
        {
            if (!Guid.TryParse(Path.GetFileName(dir), out var instanceId))
                continue;

            Instance? instance;
            try
            {
                instance = await GetByIdAsync(instanceId).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }

            if (instance is not null)
                instances.Add(instance);
        }

        return instances;
    }

    /// <summary>
    /// Returns the instance with the given ID, or null if it does not exist.
    /// </summary>
    public async Task<Instance?> GetByIdAsync(Guid instanceId)
        => await GetByIdCoreAsync(instanceId).ConfigureAwait(false);

    private async Task<Instance?> GetByIdCoreAsync(Guid instanceId)
    {
        var path = _pathProvider.GetInstanceMetadataPath(instanceId);
        var dto = await TomlFileStore.ReadAsync<InstanceDto>(path).ConfigureAwait(false);
        return dto is null ? null : InstanceMapper.FromDto(dto);
    }


    /// <summary>
    /// Returns true if the given name is not already in use by another instance (excluding the one with excludingInstanceId, if provided).
    /// </summary>
    public async Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null)
    {
        var all = await GetAllAsync().ConfigureAwait(false);
        return !all.Any(i =>
            i.InstanceId != excludingInstanceId &&
            string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a new instance with the given name and source, and saves it to disk. Throws if the name is already in use.
    /// </summary>
    public async Task<Instance> CreateAsync(string name, InstanceSource source)
    {
        if (!await IsNameAvailableAsync(name).ConfigureAwait(false))
            throw new InvalidOperationException($"Instance name '{name}' is already in use.");

        var instance = new Instance(name, source);
        await SaveAsync(instance).ConfigureAwait(false);
        return instance;
    }

    /// <summary>
    /// Renames the instance with the given ID to the new name, and saves it to disk. Throws if the instance does not exist or if the new name is already in use.
    /// </summary>
    public async Task RenameAsync(Guid instanceId, string newName)
    {
        if (!await IsNameAvailableAsync(newName, excludingInstanceId: instanceId).ConfigureAwait(false))
            throw new InvalidOperationException($"Instance name '{newName}' is already in use.");

        await UpdateAsync(
            instanceId,
            instance =>
            {
                instance.Rename(newName);
                return true;
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the instance with the given ID from disk. No-op if the instance does not exist.
    /// </summary>
    public async Task DeleteAsync(Guid instanceId)
    {
        var gate = GetInstanceLock(instanceId);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var root = _pathProvider.GetInstanceRoot(instanceId);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Saves the given instance to disk, overwriting any existing metadata. Throws if the instance is null.
    /// </summary>
    public async Task SaveAsync(Instance instance)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        var gate = GetInstanceLock(instance.InstanceId);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(instance).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TResult> UpdateAsync<TResult>(
        Guid instanceId,
        Func<Instance, TResult> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var gate = GetInstanceLock(instanceId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var instance = await GetByIdCoreAsync(instanceId).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");
            cancellationToken.ThrowIfCancellationRequested();
            var result = update(instance);
            cancellationToken.ThrowIfCancellationRequested();
            await SaveCoreAsync(instance).ConfigureAwait(false);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private Task SaveCoreAsync(Instance instance)
    {
        var dto = InstanceMapper.ToDto(instance);
        var path = _pathProvider.GetInstanceMetadataPath(instance.InstanceId);
        return TomlFileStore.WriteAsync(path, dto);
    }

    private SemaphoreSlim GetInstanceLock(Guid instanceId)
        => _instanceLocks.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Returns the ID of the currently active instance, or null if no instance is active.
    /// </summary>
    public async Task<Guid?> GetActiveInstanceIdAsync()
    {
        var path = _pathProvider.GetActiveInstancePointerPath();
        var dto = await TomlFileStore.ReadAsync<ActiveInstancePointerDto>(path).ConfigureAwait(false);

        if (dto?.ActiveInstanceId is null)
            return null;

        return Guid.TryParse(dto.ActiveInstanceId, out var id) ? id : null;
    }

    /// <summary>
    /// Sets the currently active instance to the one with the given ID. Throws if no such instance exists.
    /// </summary>
    public async Task SetActiveInstanceAsync(Guid instanceId)
    {
        var exists = await GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");

        var dto = new ActiveInstancePointerDto { ActiveInstanceId = instanceId.ToString() };
        var path = _pathProvider.GetActiveInstancePointerPath();
        await TomlFileStore.WriteAsync(path, dto).ConfigureAwait(false);
    }
}
