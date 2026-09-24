using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Files;

namespace Borea.Storage.Mods;

/// <summary>
/// File-backed implementation of IModUninstaller. A linked mod loses only its
/// link, and its stored release goes when no other instance links to it.
/// </summary>
public sealed class FileModUninstaller : IModUninstaller
{
    private readonly IGamePathProvider _pathProvider;
    private readonly Borea.Core.Instances.IInstanceRepository _instances;
    private readonly ModStore _store;

    public FileModUninstaller(IGamePathProvider pathProvider, Borea.Core.Instances.IInstanceRepository instances, ModStore? store = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _store = store ?? new ModStore(pathProvider, new DirectoryLinker(), linksReleases: false);
    }

    public async Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        cancellationToken.ThrowIfCancellationRequested();
        var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false);
        if (instance?.Mods.Any(mod => ModIds.Equals(mod.ModId, modId)) != true)
            return;

        // The folder and the record change while other changes to the instance
        // wait, so an install or a replacement never sees one without the other.
        InstalledMod? removed = null;
        await _instances.UpdateAsync(
            instanceId,
            current =>
            {
                var installed = current.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));
                if (installed is null || installed.Ownership == ModInstallOwnership.Foreign)
                    return false;

                if (!installed.CanDeleteFiles)
                {
                    throw new InvalidOperationException(
                        $"Borea cannot verify ownership of the installed folder for '{installed.ModId}'. Remove it manually or install it again before uninstalling it.");
                }

                var modDirectory = ModFolders.FindOwned(_pathProvider.GetInstanceModsFolder(instanceId), installed, _store);
                cancellationToken.ThrowIfCancellationRequested();
                if (modDirectory is not null)
                    DirectoryLinks.DeleteTreeWithoutFollowingLinks(modDirectory);

                removed = installed;
                return current.RemoveMod(installed.ModId);
            },
            cancellationToken).ConfigureAwait(false);

        if (removed is not null)
            await _store.ReleaseAsync(removed).ConfigureAwait(false);
    }
}
