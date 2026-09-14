using Borea.Core.Instances;
using Borea.Core.Paths;

namespace Borea.Storage.Instances;

public sealed class FileGameDataReader : IGameDataReader
{
    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly IGamePathProvider _pathProvider;

    public FileGameDataReader(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task<IReadOnlyList<GameDataEntry>> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => Task.Run(() => Read(instanceId, cancellationToken), cancellationToken);

    private IReadOnlyList<GameDataEntry> Read(Guid instanceId, CancellationToken cancellationToken) =>
    [
        Folder(_pathProvider.GetInstanceSavesFolder(instanceId), cancellationToken),
        Folder(_pathProvider.GetInstanceVehiclesFolder(instanceId), cancellationToken),
        File(_pathProvider.GetInstanceSettingsPath(instanceId)),
        Folder(_pathProvider.GetInstanceHudLayoutsFolder(instanceId), cancellationToken),
        Folder(_pathProvider.GetInstanceCrashDumpsFolder(instanceId), cancellationToken),
        Folder(_pathProvider.GetInstanceExportsFolder(instanceId), cancellationToken),
    ];

    private static GameDataEntry Folder(string path, CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(path);
        if (!folder.Exists)
            return new GameDataEntry(folder.Name, path, IsFolder: true, Exists: false, SizeBytes: 0);

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
            return new GameDataEntry(folder.Name, path, IsFolder: true, Exists: false, SizeBytes: 0);
        }

        return new GameDataEntry(folder.Name, path, IsFolder: true, Exists: true, size);
    }

    private static GameDataEntry File(string path)
    {
        var file = new FileInfo(path);
        return new GameDataEntry(file.Name, path, IsFolder: false, file.Exists, file.Exists ? file.Length : 0);
    }
}
