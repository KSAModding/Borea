using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Instances;

/// <summary>
/// Adds up the files under the instance folder and under each mod folder,
/// the way <see cref="FileGameDataReader"/> does for the game data rows.
/// </summary>
public sealed class FileInstanceSizeReader : IInstanceSizeReader
{
    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly IGamePathProvider _pathProvider;

    public FileInstanceSizeReader(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task<InstanceSizes> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => Task.Run(() => Read(instanceId, cancellationToken), cancellationToken);

    private InstanceSizes Read(Guid instanceId, CancellationToken cancellationToken)
    {
        var mods = new Dictionary<string, long>(ModIds.Comparer);
        var modsFolder = new DirectoryInfo(_pathProvider.GetInstanceModsFolder(instanceId));
        if (modsFolder.Exists)
        {
            foreach (var folder in modsFolder.EnumerateDirectories())
                mods[folder.Name] = Size(folder, cancellationToken);
        }

        return new InstanceSizes(Size(new DirectoryInfo(_pathProvider.GetInstanceRoot(instanceId)), cancellationToken), mods);
    }

    private static long Size(DirectoryInfo folder, CancellationToken cancellationToken)
    {
        if (!folder.Exists)
            return 0;

        long size = 0;
        try
        {
            foreach (var file in folder.EnumerateFiles("*", AllFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                size += file.Length;
            }
        }
        catch (DirectoryNotFoundException)
        {
            // removed while it was measured; the next read sees the new state
        }

        return size;
    }
}
