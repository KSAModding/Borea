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

    private static readonly EnumerationOptions TopFiles = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Paths compare the way the file system does: without case on Windows and macOS.</summary>
    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private readonly IGamePathProvider _pathProvider;

    public FileInstanceSizeReader(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task<InstanceSizes> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => Task.Run(() => Read(instanceId, cancellationToken), cancellationToken);

    /// <summary>
    /// One walk of the instance: the mod folders are measured on their own
    /// and added to the total, and the walk of the root skips the mods folder.
    /// </summary>
    private InstanceSizes Read(Guid instanceId, CancellationToken cancellationToken)
    {
        var mods = new Dictionary<string, long>(ModIds.Comparer);
        var modsFolder = new DirectoryInfo(_pathProvider.GetInstanceModsFolder(instanceId));
        var root = new DirectoryInfo(_pathProvider.GetInstanceRoot(instanceId));
        long total = 0;

        total += Size(root, cancellationToken, skip: modsFolder);
        if (modsFolder.Exists)
        {
            total += Size(modsFolder, cancellationToken, skip: null, recurse: false);
            foreach (var folder in Children(modsFolder))
            {
                var size = Size(folder, cancellationToken);
                mods[folder.Name] = size;
                total += size;
            }
        }

        return new InstanceSizes(total, mods);
    }

    /// <summary>
    /// The size of <paramref name="folder"/>. With <paramref name="skip"/>, a
    /// child folder of that name is left out; without recursion, only the files
    /// directly in it count.
    /// </summary>
    private static long Size(DirectoryInfo folder, CancellationToken cancellationToken, DirectoryInfo? skip = null, bool recurse = true)
    {
        if (!folder.Exists)
            return 0;

        if (skip is null)
            return Files(folder, recurse ? AllFiles : TopFiles, cancellationToken);

        var size = Files(folder, TopFiles, cancellationToken);
        foreach (var child in Children(folder))
        {
            if (!string.Equals(child.FullName, skip.FullName, PathComparison))
                size += Files(child, AllFiles, cancellationToken);
        }

        return size;
    }

    private static long Files(DirectoryInfo folder, EnumerationOptions options, CancellationToken cancellationToken)
    {
        long size = 0;
        try
        {
            foreach (var file in folder.EnumerateFiles("*", options))
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

    private static IEnumerable<DirectoryInfo> Children(DirectoryInfo folder)
    {
        try
        {
            return folder.EnumerateDirectories("*", TopFiles).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

}
