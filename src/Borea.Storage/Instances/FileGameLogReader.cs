using Borea.Core.Instances;
using Borea.Core.Paths;
using Borea.Storage.Files;

namespace Borea.Storage.Instances;

public sealed class FileGameLogReader : IGameLogReader
{
    public const int MaxLines = 200;

    private const int MaxBytes = 64 * 1024;

    private readonly IGamePathProvider _pathProvider;

    public FileGameLogReader(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task<GameLogTail> ReadGameLogAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var path = _pathProvider.GetInstanceGameLogPath(instanceId);
        return Task.Run(() => Read(CurrentLog(path) ?? path), cancellationToken);
    }

    public Task<DateTimeOffset?> GetLastWriteAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var path = _pathProvider.GetInstanceGameLogPath(instanceId);
        return Task.Run(
            () => GameLogFiles.Find(path)
                .Select(log => (DateTimeOffset?)new DateTimeOffset(log.File.LastWriteTimeUtc, TimeSpan.Zero))
                .Max(),
            cancellationToken);
    }

    private static string? CurrentLog(string gameLogPath) => GameLogFiles.Find(gameLogPath)
        .Where(log => log.Kind != GameLogKind.Archive)
        .MaxBy(log => log.File.LastWriteTimeUtc)?
        .File.FullName;

    private static GameLogTail Read(string path)
    {
        try
        {
            return new GameLogTail(path, Exists: true, LogFileTail.ReadLastLines(path, MaxBytes, MaxLines));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new GameLogTail(path, Exists: false, []);
        }
    }
}
