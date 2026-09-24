using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Launch;

namespace Borea.Storage.Mods;

/// <summary>
/// File-backed <see cref="ISharedModStore"/>. A break-out copies the stored
/// release into each instance that links to it and swaps the link and the
/// record together, so a folder and its record never disagree. The entry goes
/// once no link leads to it.
/// </summary>
public sealed class FileSharedModStore : ISharedModStore
{
    private static readonly TimeSpan DefaultGamePollInterval = TimeSpan.FromSeconds(5);

    private readonly IGamePathProvider _paths;
    private readonly ModStore _store;
    private readonly IInstanceRepository _instances;
    private readonly IBoreaSettingsRepository _settings;
    private readonly ILauncher _launcher;
    private readonly Func<bool> _isGameProcessRunning;
    private readonly Func<bool> _isOtherBoreaRunning;
    private readonly TimeSpan _gamePollInterval;

    /// <summary>Two break-outs of the same entry would copy from an entry the first one removes.</summary>
    private readonly SemaphoreSlim _breakOutLock = new(1, 1);

    /// <param name="isGameProcessRunning">Null looks for a KSA or StarMap process.</param>
    /// <param name="isOtherBoreaRunning">Null looks for another Borea App or command.</param>
    public FileSharedModStore(
        IGamePathProvider paths,
        ModStore store,
        IInstanceRepository instances,
        IBoreaSettingsRepository settings,
        ILauncher launcher,
        Func<bool>? isGameProcessRunning = null,
        Func<bool>? isOtherBoreaRunning = null)
        : this(paths, store, instances, settings, launcher, isGameProcessRunning, isOtherBoreaRunning, DefaultGamePollInterval)
    {
    }

    internal FileSharedModStore(
        IGamePathProvider paths,
        ModStore store,
        IInstanceRepository instances,
        IBoreaSettingsRepository settings,
        ILauncher launcher,
        Func<bool>? isGameProcessRunning,
        Func<bool>? isOtherBoreaRunning,
        TimeSpan gamePollInterval)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _isGameProcessRunning = isGameProcessRunning ?? RunningProcesses.IsGameRunning;
        _isOtherBoreaRunning = isOtherBoreaRunning ?? RunningProcesses.IsOtherBoreaRunning;
        _gamePollInterval = gamePollInterval;
    }

    public async Task<IReadOnlyList<InstalledMod>> CheckAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        await _breakOutLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CheckLockedAsync(instanceId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _breakOutLock.Release();
        }
    }

    private async Task<IReadOnlyList<InstalledMod>> CheckLockedAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        if (await _instances.GetByIdAsync(instanceId).ConfigureAwait(false) is not { } instance)
            return [];

        var changed = new List<InstalledMod>();
        foreach (var mod in instance.Mods.Where(mod => mod.Storage == ModStorage.Linked))
        {
            if (await _store.HasChangedAsync(_store.EntryPath(mod), cancellationToken).ConfigureAwait(false))
                changed.Add(mod);
        }

        // the game can hold the files open, and it may still be writing
        if (changed.Count == 0 || await IsGameRunningAsync().ConfigureAwait(false))
            return [];

        var brokenOut = new List<InstalledMod>();
        foreach (var mod in changed)
        {
            await BreakOutAsync(_store.EntryPath(mod), cancellationToken).ConfigureAwait(false);
            if (await _store.MakePrivateAsync(mod.ModId, cancellationToken).ConfigureAwait(false))
                brokenOut.Add(mod);
        }

        return brokenOut;
    }

    public async Task WaitForGameExitAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        while (_launcher.IsRunning(instanceId) || _isGameProcessRunning())
            await Task.Delay(_gamePollInterval, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The setting is saved before the links are broken out, so a Borea started
    /// meanwhile makes no new link. Turning it off again repeats a break-out
    /// that stopped part way.
    /// </summary>
    public async Task<SharedModStoreChange> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled && await IsGameRunningAsync().ConfigureAwait(false))
            return SharedModStoreChange.GameRunning;

        if (!enabled && _isOtherBoreaRunning())
            return SharedModStoreChange.BoreaRunning;

        var saved = await _settings.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? new BoreaSettings(gameDirectoryPath: null);
        if (saved.SharedModStore != enabled)
            await _settings.SaveAsync(saved.WithSharedModStore(enabled), cancellationToken).ConfigureAwait(false);

        if (enabled)
            return SharedModStoreChange.Saved;

        await _breakOutLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var entry in _store.Entries())
                await BreakOutAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _breakOutLock.Release();
        }

        return SharedModStoreChange.Saved;
    }

    private async Task<bool> IsGameRunningAsync()
        => _isGameProcessRunning() || (await _instances.GetAllAsync().ConfigureAwait(false)).Any(instance => _launcher.IsRunning(instance.InstanceId));

    private async Task BreakOutAsync(string entry, CancellationToken cancellationToken)
    {
        foreach (var instance in await _instances.GetAllAsync().ConfigureAwait(false))
        {
            if (instance.Mods.Any(mod => _store.IsStoredIn(mod, entry)))
                await BreakOutAsync(instance.InstanceId, entry, cancellationToken).ConfigureAwait(false);
        }

        await _store.ReleaseAsync([entry]).ConfigureAwait(false);
    }

    /// <summary>
    /// The link moves aside before the copy takes its place, and moves back
    /// when the record cannot be saved. A link that cannot move back stays
    /// aside and the error names it. A mod whose record or link changed
    /// meanwhile is left as it is.
    /// </summary>
    private async Task BreakOutAsync(Guid instanceId, string entry, CancellationToken cancellationToken)
    {
        var instanceRoot = _paths.GetInstanceRoot(instanceId);
        var stagingFolder = Path.Combine(instanceRoot, $".borea-staging-{Guid.NewGuid():N}");
        var recoveryLink = Path.Combine(instanceRoot, $".borea-recovery-{Guid.NewGuid():N}");
        string? modFolder = null;
        string? modId = null;
        var keepRecoveryLink = false;
        try
        {
            ModFolders.CopyWithoutMarker(entry, stagingFolder, cancellationToken);
            var ownershipToken = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(stagingFolder, ModFolders.OwnershipFileName), ownershipToken, cancellationToken).ConfigureAwait(false);

            await _instances.UpdateAsync(
                instanceId,
                current =>
                {
                    var linked = current.Mods.FirstOrDefault(mod => _store.IsStoredIn(mod, entry));
                    if (linked is null || ModFolders.FindOwned(_paths.GetInstanceModsFolder(instanceId), linked, _store) is not { } folder)
                        return false;

                    Directory.Move(folder, recoveryLink);
                    modFolder = folder;
                    modId = linked.ModId;
                    Directory.Move(stagingFolder, folder);
                    current.ReplaceMod(new InstalledMod(
                        linked.ModId,
                        linked.Version,
                        linked.Reason,
                        linked.InstalledAt,
                        linked.Metadata,
                        linked.Checksum,
                        ModInstallOwnership.Borea,
                        ownershipToken,
                        ModStorage.Private));
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception operationError) when (modFolder is not null)
        {
            try
            {
                DirectoryLinks.DeleteTreeWithoutFollowingLinks(modFolder);
                Directory.Move(recoveryLink, modFolder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                keepRecoveryLink = true;
                throw new IOException($"Borea could not give {modId} its own copy, and could not put its link back. The link is at {recoveryLink}.", operationError);
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingFolder);
            if (!keepRecoveryLink)
                TryDeleteDirectory(recoveryLink);
        }
    }

    /// <summary>Cleanup that must not replace the outcome. A link is removed as a link.</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DirectoryLinks.DeleteTreeWithoutFollowingLinks(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
