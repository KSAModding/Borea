using Borea.Core.Instances;
using Borea.Core.Logging;
using Borea.Core.Paths;
using Borea.Storage.Instances;

namespace Borea.Storage.Logging;

public sealed class LoggingInstanceRepository : IInstanceRepository
{
    private readonly IGamePathProvider _paths;
    private readonly IBoreaLog _log;

    public IInstanceRepository Inner { get; }

    public LoggingInstanceRepository(IInstanceRepository inner, IGamePathProvider paths, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<IReadOnlyList<Instance>> GetAllAsync() => Inner.GetAllAsync();

    public Task<Instance?> GetByIdAsync(Guid instanceId) => Inner.GetByIdAsync(instanceId);

    public Task<Guid?> GetActiveInstanceIdAsync() => Inner.GetActiveInstanceIdAsync();

    public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => Inner.IsNameAvailableAsync(name, excludingInstanceId);

    public Task SaveAsync(Instance instance) => Inner.SaveAsync(instance);

    public Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
        => Inner.UpdateAsync(instanceId, update, cancellationToken);

    public Task<InstanceCreateResult> CreateAsync(string name, InstanceSource source)
        => CreateAsync(new Instance(name, source));

    public Task<InstanceCreateResult> CreateAsync(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return CreateAsync(instance, instance.Source is InstanceSource.FromModPack ? InstanceOrigin.ModPack : InstanceOrigin.New);
    }

    public async Task<InstanceCreateResult> CreateAsync(Instance instance, InstanceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var action = $"Creation of instance {Describe(instance.InstanceId, instance.Name)} {DescribeOrigin(instance, origin)}";
        InstanceCreateResult result;
        try
        {
            result = await Inner.CreateAsync(instance, origin).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteFailure(action, exception);
            throw;
        }

        _log.Write($"Instance {Describe(instance.InstanceId, instance.Name)} created {DescribeOrigin(instance, origin)}{(result.Activated ? " and made active" : "")}.");
        return result;
    }

    public async Task RenameAsync(Guid instanceId, string newName)
    {
        var name = Describe(instanceId, await TryGetNameAsync(instanceId).ConfigureAwait(false));
        try
        {
            await Inner.RenameAsync(instanceId, newName).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteFailure($"Rename of instance {name} to \"{newName}\"", exception);
            throw;
        }

        _log.Write($"Instance {name} renamed to \"{newName}\".");
    }

    public async Task DeleteAsync(Guid instanceId)
    {
        var knownName = await TryGetNameAsync(instanceId).ConfigureAwait(false);
        var folder = _paths.GetInstanceRoot(instanceId);
        if (knownName is null && !Directory.Exists(folder))
        {
            var backups = GameSaveBackupFolder.InstanceFolder(_paths, instanceId);
            var hadBackups = Directory.Exists(backups);
            await Inner.DeleteAsync(instanceId).ConfigureAwait(false);
            _log.Write(hadBackups
                ? $"Instance {instanceId} not found, its backups in {backups} deleted."
                : $"Instance {instanceId} not found, nothing deleted.");
            return;
        }

        var name = Describe(instanceId, knownName);
        var wasActive = await TryAsync(Inner.GetActiveInstanceIdAsync).ConfigureAwait(false) == instanceId;
        _log.Write($"Deletion of instance {name} in {folder} started.");
        try
        {
            await Inner.DeleteAsync(instanceId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log.Write($"Deletion of instance {name} {await DescribeDeleteFailureAsync(instanceId).ConfigureAwait(false)}{await DeactivationNoteAsync(wasActive).ConfigureAwait(false)}.", exception);
            throw;
        }

        _log.Write($"Instance {name} deleted{await DeactivationNoteAsync(wasActive).ConfigureAwait(false)}.");
    }

    public async Task SetActiveInstanceAsync(Guid instanceId)
    {
        var name = Describe(instanceId, await TryGetNameAsync(instanceId).ConfigureAwait(false));
        try
        {
            await Inner.SetActiveInstanceAsync(instanceId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteFailure($"Activation of instance {name}", exception);
            throw;
        }

        _log.Write($"Instance {name} activated.");
    }

    public async Task ClearActiveInstanceAsync()
    {
        var activeId = await TryAsync(Inner.GetActiveInstanceIdAsync).ConfigureAwait(false);
        var name = activeId is { } id ? Describe(id, await TryGetNameAsync(id).ConfigureAwait(false)) : null;
        try
        {
            await Inner.ClearActiveInstanceAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log.Write(name is null ? "Deactivation of the active instance failed." : $"Deactivation of instance {name} failed.", exception);
            throw;
        }

        if (name is not null)
            _log.Write($"Instance {name} deactivated.");
    }

    // a taken name or an unknown id is refused with InvalidOperationException, a user error that needs no stack trace
    private void WriteFailure(string action, Exception exception)
    {
        if (exception is InvalidOperationException)
            _log.Write($"{action} refused: {exception.Message}");
        else
            _log.Write($"{action} failed.", exception);
    }

    private async Task<string> DescribeDeleteFailureAsync(Guid instanceId)
    {
        try
        {
            return await Inner.GetByIdAsync(instanceId).ConfigureAwait(false) is null
                ? "failed part way, Borea no longer lists it"
                : "failed";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "failed, Borea cannot read what is left";
        }
    }

    private async Task<string> DeactivationNoteAsync(bool wasActive)
    {
        if (!wasActive)
            return "";

        try
        {
            return await Inner.GetActiveInstanceIdAsync().ConfigureAwait(false) is null ? ", no instance is active now" : "";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "";
        }
    }

    private static string Describe(Guid instanceId, string? name)
        => name is null ? instanceId.ToString() : $"\"{name}\" ({instanceId})";

    private static string DescribeOrigin(Instance instance, InstanceOrigin origin) => origin switch
    {
        InstanceOrigin.Duplicate => "as a duplicate",
        InstanceOrigin.ModListImport => "from a modlist",
        InstanceOrigin.GameProfileImport => "from the game profile",
        InstanceOrigin.ModPack when instance.Source is InstanceSource.FromModPack pack => $"from mod pack {pack.ModPackId} {pack.Version}",
        InstanceOrigin.ModPack => "from a mod pack",
        _ => "as a new instance",
    };

    private async Task<string?> TryGetNameAsync(Guid instanceId)
        => (await TryAsync(() => Inner.GetByIdAsync(instanceId)).ConfigureAwait(false))?.Name;

    // a lookup only adds detail to a line, so its failure must not change the operation
    private static async Task<T?> TryAsync<T>(Func<Task<T?>> lookup)
    {
        try
        {
            return await lookup().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return default;
        }
    }
}
