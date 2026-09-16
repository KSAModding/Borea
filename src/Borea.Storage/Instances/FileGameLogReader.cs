using System.Text;
using Borea.Core.Instances;
using Borea.Core.Paths;

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
            // shared, because a running game keeps its log open
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - MaxBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: start == 0);

            var lines = reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            if (start > 0)
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            return new GameLogTail(path, Exists: true, lines.TakeLast(MaxLines).ToList());
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new GameLogTail(path, Exists: false, []);
        }
    }
}
