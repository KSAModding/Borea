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

        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        if (!Directory.Exists(modsFolder))
            return Task.CompletedTask;

        // Ids compare case-insensitively while folder names are case-sensitive
        // on Linux and macOS, so the folder is resolved by comparison, not by path.
        var modDirectory = Directory.GetDirectories(modsFolder)
            .FirstOrDefault(d => ModIds.Equals(Path.GetFileName(d), modId));

        if (modDirectory is not null)
            Directory.Delete(modDirectory, recursive: true);

        return Task.CompletedTask;
    }
}
