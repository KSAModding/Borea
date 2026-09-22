using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Mods;

/// <summary>
/// Compares the recorded mods of an instance with the folders under its mods
/// folder. The folders are read once per scan, because an instance can hold
/// many mods.
/// </summary>
public sealed class FileMissingModDetector : IMissingModDetector
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IInstanceRepository _instances;

    public FileMissingModDetector(IGamePathProvider pathProvider, IInstanceRepository instances)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
    }

    public async Task<IReadOnlyList<string>> ScanAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");

        cancellationToken.ThrowIfCancellationRequested();
        var present = ModFoldersOnDisk(instanceId);
        return instance.Mods
            .Where(mod => !present.Contains(mod.ModId))
            .Select(mod => mod.ModId)
            .ToList();
    }

    public async Task<bool> DropAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        return await _instances.UpdateAsync(
            instanceId,
            instance =>
            {
                var installed = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));

                // read inside the update, so a folder that came back while the
                // user looked at the page keeps its record
                if (installed is null || ModFoldersOnDisk(instanceId).Contains(installed.ModId))
                    return false;

                return instance.RemoveMod(installed.ModId);
            },
            cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// The names of the folders the game would load a mod from, which are the
    /// ones with a mod.toml (ModLibrary.AddMods, ModEntry.Exists).
    /// </summary>
    private HashSet<string> ModFoldersOnDisk(Guid instanceId)
    {
        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        var names = new HashSet<string>(ModIds.Comparer);
        if (!Directory.Exists(modsFolder))
            return names;

        foreach (var directory in Directory.EnumerateDirectories(modsFolder))
        {
            if (File.Exists(Path.Combine(directory, ModFolders.DefinitionFileName)))
                names.Add(Path.GetFileName(directory));
        }

        return names;
    }
}
