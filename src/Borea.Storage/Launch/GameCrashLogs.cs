using System.Text;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Storage.Instances;

namespace Borea.Storage.Launch;

internal static class GameCrashLogs
{
    /// <summary>
    /// The crash in the log, or null while it cannot be read. It is opened
    /// without write sharing, so on Windows a log that the monitor of the game
    /// still writes is read on a later try and never half.
    /// </summary>
    public static GameCrash? Read(GameCrashLogFile log, Instance instance, string modsFolder)
    {
        string[] lines;
        try
        {
            using var stream = new FileStream(log.File.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            lines = reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // a crash log does not say which loader started the game, so every known loader is asked
        var afterShutdown = LoaderProfiles.Known.All.Any(profile => profile.FailedAfterTheGameShutDown(lines));
        var blamed = afterShutdown ? null : ModBlame.Find(instance, modsFolder, GameCrashReport.AssemblyNames(lines));
        return new GameCrash(
            log.File.FullName,
            log.Run,
            new DateTimeOffset(log.File.LastWriteTimeUtc, TimeSpan.Zero),
            GameCrashReport.ExceptionText(lines),
            blamed?.ModId,
            afterShutdown);
    }
}
