using Borea.Core.Instances;
using Borea.Core.Paths;
using Borea.Core.Screenshots;

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
    private readonly IScreenshotStore _screenshots;

    public FileGameDataReader(IGamePathProvider pathProvider, IScreenshotStore screenshots)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _screenshots = screenshots;
    }

    public async Task<IReadOnlyList<GameDataEntry>> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var screenshotsFolder = _pathProvider.GetInstanceScreenshotsFolder(instanceId);
        var screenshots = await _screenshots.ListAsync(screenshotsFolder, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => Read(instanceId, screenshotsFolder, screenshots, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<GameDataEntry> Read(Guid instanceId, string screenshotsFolder, IReadOnlyList<Screenshot> screenshots, CancellationToken cancellationToken) =>
    [
        File(GameDataKind.Settings, _pathProvider.GetInstanceSettingsPath(instanceId)),
        Folder(GameDataKind.HudLayouts, _pathProvider.GetInstanceHudLayoutsFolder(instanceId), cancellationToken),
        Folder(GameDataKind.CrashDumps, _pathProvider.GetInstanceCrashDumpsFolder(instanceId), cancellationToken),
        new GameDataEntry(GameDataKind.Screenshots, Path.GetFileName(screenshotsFolder), screenshotsFolder, IsFolder: true, Directory.Exists(screenshotsFolder), screenshots.Sum(screenshot => screenshot.SizeBytes), screenshots.Count),
        Folder(GameDataKind.Exports, _pathProvider.GetInstanceExportsFolder(instanceId), cancellationToken, without: screenshotsFolder),
    ];

    private static GameDataEntry Folder(GameDataKind kind, string path, CancellationToken cancellationToken, string? without = null)
    {
        var folder = new DirectoryInfo(path);
        if (!folder.Exists)
            return new GameDataEntry(kind, folder.Name, path, IsFolder: true, Exists: false, SizeBytes: 0);

        var skipped = without is null ? null : Path.GetFullPath(without) + Path.DirectorySeparatorChar;
        long size = 0;
        try
        {
            foreach (var file in folder.EnumerateFiles("*", AllFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (skipped is null || !file.FullName.StartsWith(skipped, StringComparison.Ordinal))
                    size += file.Length;
            }
        }
        catch (DirectoryNotFoundException)
        {
            return new GameDataEntry(kind, folder.Name, path, IsFolder: true, Exists: false, SizeBytes: 0);
        }

        return new GameDataEntry(kind, folder.Name, path, IsFolder: true, Exists: true, size);
    }

    private static GameDataEntry File(GameDataKind kind, string path)
    {
        var file = new FileInfo(path);
        return new GameDataEntry(kind, file.Name, path, IsFolder: false, file.Exists, file.Exists ? file.Length : 0);
    }
}
