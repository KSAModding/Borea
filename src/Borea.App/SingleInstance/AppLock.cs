using System;
using System.IO;

namespace Borea.App.SingleInstance;

/// <summary>
/// The lock file the running App holds open without sharing. The operating system
/// releases it when the process ends, also after a crash, so a stale file blocks no start.
/// </summary>
internal sealed class AppLock : IDisposable
{
    private readonly FileStream _file;

    private AppLock(FileStream file)
    {
        _file = file;
    }

    public static AppLock? TryAcquire(string path, out Exception? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            // Without FileShare.Inheritable the handle does not pass to a game that Borea starts,
            // so the lock ends with Borea and not with the game.
            var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            error = null;
            return new AppLock(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception;
            return null;
        }
    }

    public void Dispose() => _file.Dispose();
}
