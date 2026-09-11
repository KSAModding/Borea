using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Mods;

/// <summary>
/// File-backed implementation of IModUninstaller.
/// </summary>
public sealed class FileModUninstaller : IModUninstaller
{
    private readonly IGamePathProvider _pathProvider;

    public FileModUninstaller(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        var modDirectory = ModFolders.Find(_pathProvider.GetInstanceModsFolder(instanceId), modId);
        if (modDirectory is not null)
            Directory.Delete(modDirectory, recursive: true);

        return Task.CompletedTask;
    }
}
