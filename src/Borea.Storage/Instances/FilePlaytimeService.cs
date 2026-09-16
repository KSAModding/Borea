using System.Collections.Concurrent;
using Borea.Core.Instances;
using Borea.Core.Paths;
using Borea.Storage.Toml;

namespace Borea.Storage.Instances;

/// <summary>
/// Sums the sessions of the game logs in an instance, and keeps each archived
/// session in playtime.toml because Brutal.Monitor LogArchiver.Rotate deletes
/// all but the newest archives.
/// </summary>
public sealed class FilePlaytimeService : IPlaytimeService
{
    /// <summary>A log without a shutdown line that was written this recently belongs to a game that still runs.</summary>
    public static readonly TimeSpan RunningWindow = TimeSpan.FromMinutes(10);

    private readonly IGamePathProvider _pathProvider;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _instanceLocks = new();

    public FilePlaytimeService(IGamePathProvider pathProvider, TimeProvider? time = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _time = time ?? TimeProvider.System;
    }

    public async Task<InstancePlaytime> GetPlaytimeAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var gate = _instanceLocks.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cachePath = _pathProvider.GetInstancePlaytimePath(instanceId);
            var (cache, writable) = await ReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
            var gameLogPath = _pathProvider.GetInstanceGameLogPath(instanceId);
            var (playtime, cacheChanged) = await Task.Run(() => Tally(gameLogPath, cache, cancellationToken), cancellationToken).ConfigureAwait(false);

            if (cacheChanged && writable)
                await WriteCacheAsync(cachePath, cache, cancellationToken).ConfigureAwait(false);

            return playtime;
        }
        finally
        {
            gate.Release();
        }
    }

    private (InstancePlaytime Playtime, bool CacheChanged) Tally(string gameLogPath, List<CachedSessionDto> cache, CancellationToken cancellationToken)
    {
        var logs = GameLogFiles.Find(gameLogPath);
        var newest = logs.MaxBy(log => log.File.LastWriteTimeUtc);
        var current = new List<GameSession>();
        var cacheChanged = false;
        var unreadable = 0;
        var isKnown = true;
        var includesRunningSession = false;

        foreach (var log in logs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = log.File.Name;
            if (log.Kind == GameLogKind.Archive && cache.Any(entry => entry.File == name && entry.Size == log.File.Length))
                continue;

            if (TryRead(log) is not { } read)
                continue;

            if (!read.Readable)
            {
                unreadable++;
                isKnown &= log != newest;
                continue;
            }

            if (read.Session is not { } session)
                continue;

            if (log.Kind == GameLogKind.Archive)
            {
                // the same name and start is the same archive read again, anything else is a later archive under a reused name
                cache.RemoveAll(entry => entry.File == name && entry.Start == session.Start);
                cache.Add(new CachedSessionDto { File = name, Size = log.File.Length, Start = session.Start, End = session.End, Clean = session.ClosedCleanly });
                cacheChanged = true;
                continue;
            }

            current.Add(session);
            includesRunningSession |= session.Counts
                && !session.ClosedCleanly
                && _time.GetUtcNow() - log.File.LastWriteTimeUtc < RunningWindow;
        }

        var counted = cache
            .Select(entry => new GameSession(entry.Start, entry.End, entry.Clean))
            .Concat(current)
            .Where(session => session.Counts)
            .ToList();
        var total = TimeSpan.FromTicks(counted.Sum(session => session.Length.Ticks));
        return (new InstancePlaytime(total, counted.Count, includesRunningSession, unreadable, isKnown), cacheChanged);
    }

    /// <summary>Null when the log moved into the archives or was deleted while it was read.</summary>
    private static GameSessionLog? TryRead(GameLogFile log)
    {
        try
        {
            return GameSessionLogReader.Read(log);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new GameSessionLog(null, Readable: false);
        }
    }

    /// <summary>
    /// Writable is false when the cache is there but cannot be read, because a
    /// new cache from the archives that are left would lose the older sessions.
    /// A cache that does not parse is kept as playtime.toml.bad.
    /// </summary>
    private static async Task<(List<CachedSessionDto> Sessions, bool Writable)> ReadCacheAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var dto = await TomlFileStore.ReadAsync<PlaytimeCacheDto>(path, cancellationToken).ConfigureAwait(false);
            return (dto?.Sessions ?? [], true);
        }
        catch (InvalidOperationException)
        {
            return ([], TryMoveAside(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ([], false);
        }
    }

    private static bool TryMoveAside(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task WriteCacheAsync(string path, List<CachedSessionDto> sessions, CancellationToken cancellationToken)
    {
        try
        {
            await TomlFileStore.WriteAsync(path, new PlaytimeCacheDto { Sessions = sessions }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the total is right without the cache, the next read only reads the archives again
        }
    }
}
