using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Mods;

/// <summary>
/// File-backed implementation of IModUninstaller.
/// </summary>
public sealed class FileModUninstaller : IModUninstaller
{
    private readonly IGamePathProvider _pathProvider;
    private readonly Borea.Core.Instances.IInstanceRepository _instances;

    public FileModUninstaller(IGamePathProvider pathProvider, Borea.Core.Instances.IInstanceRepository instances)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
    }

    public async Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        cancellationToken.ThrowIfCancellationRequested();
        var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false);
        var installed = instance?.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));
        if (installed is null || installed.Ownership == ModInstallOwnership.Foreign)
            return;

        if (!installed.CanDeleteFiles)
        {
            throw new InvalidOperationException(
                $"Borea cannot verify ownership of the installed folder for '{installed.ModId}'. Remove it manually or install it again before uninstalling it.");
        }

        var modDirectory = ModFolders.FindOwned(
            _pathProvider.GetInstanceModsFolder(instanceId),
            installed.ModId,
            installed.OwnershipToken!);
        if (modDirectory is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(modDirectory, recursive: true);
        }
    }
}
