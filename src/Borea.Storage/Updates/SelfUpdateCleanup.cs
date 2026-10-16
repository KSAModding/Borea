using System.Diagnostics;
using Borea.Core.Updates;

namespace Borea.Storage.Updates;

/// <summary>
/// A new build removes the build it replaced, once it has shown that it runs. The only thing that
/// can be deleted is the build beside this one, a program file or a folder, that carries its own
/// name and the replaced ending, and only when the receipt in this build's folder names it, so an
/// argument alone deletes nothing. A start also sweeps the unpacked builds that an update which
/// never finished left in the folder.
/// </summary>
public static class SelfUpdateCleanup
{
    /// <summary>How long the new build waits for the old one to end.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>What an update adds to the name of the build it replaces.</summary>
    public const string ReplacedEnding = ".old";

    /// <summary>The start of the name of the folder an update unpacks the new build into.</summary>
    public const string StagingPrefix = ".borea-update-";

    /// <summary>How long a staging folder must lie untouched before a sweep takes it, so that an update which runs keeps its own.</summary>
    public static readonly TimeSpan StagingAge = TimeSpan.FromHours(1);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private const int DeleteAttempts = 20;

    /// <summary>How two paths of this system are compared, which only Linux reads letter by letter.</summary>
    internal static StringComparison PathComparison
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Where <paramref name="programPath"/> is moved to while a new build takes its place.</summary>
    public static string ReplacedPath(string programPath)
    {
        ArgumentNullException.ThrowIfNull(programPath);
        return programPath + ReplacedEnding;
    }

    /// <summary>
    /// Waits for the replaced build to end and deletes it. It never throws, and it returns one line
    /// for the log that says what happened.
    /// </summary>
    /// <param name="programPath">The running program. Null reads it from the process.</param>
    public static string Run(SelfUpdateHandover handover, string? programPath = null)
    {
        ArgumentNullException.ThrowIfNull(handover);

        try
        {
            var running = programPath ?? Environment.ProcessPath;
            var previous = Path.GetFullPath(handover.PreviousProgramPath);
            if (Refusal(previous, running, handover) is { } refusal)
                return $"Self-update: the replaced build at {previous} was left alone, because {refusal}";

            WaitForExit(handover.PreviousProcessId, Path.GetFileNameWithoutExtension(running!));
            var folder = InstalledBuild(Path.GetFullPath(running!)).Folder!;

            // An update that runs owns this file as its way back, so the claim keeps this delete off it.
            using var claim = SelfUpdateLock.TryTake(folder);
            if (claim is null)
                return $"Self-update: the replaced build at {previous} stays, because another update is changing {folder}.";

            var message = Remove(previous);
            SelfUpdateReceipt.Delete(folder);
            return message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"Self-update: the replaced build could not be removed. {exception.Message}";
        }
    }

    /// <summary>
    /// Waits for the replaced build to end, without deleting anything. The App does this before it
    /// takes the single-instance lock, because the lock belongs to the build that is going away.
    /// </summary>
    /// <param name="programPath">The running program. Null reads it from the process.</param>
    public static void WaitForPreviousExit(SelfUpdateHandover handover, string? programPath = null)
    {
        ArgumentNullException.ThrowIfNull(handover);

        try
        {
            var running = programPath ?? Environment.ProcessPath;
            var previous = Path.GetFullPath(handover.PreviousProgramPath);
            if (Refusal(previous, running, handover) is not null)
                return;

            WaitForExit(handover.PreviousProcessId, Path.GetFileNameWithoutExtension(running!));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    /// <summary>
    /// Removes the unpacked builds that updates which never finished left in the folder Borea runs
    /// in. A crash, a power loss and a window that the player closed mid-update each leave one, and
    /// no other step removes them. A folder that was touched inside <see cref="StagingAge"/> stays,
    /// because an update that runs right now owns it.
    /// </summary>
    /// <param name="folder">The folder Borea runs in. Null reads it from the process.</param>
    /// <returns>One line for the log, or null when there was nothing to remove.</returns>
    public static string? SweepStaging(string? folder = null)
    {
        try
        {
            var install = folder ?? (Environment.ProcessPath is { } running ? InstalledBuild(running).Folder : null);
            if (install is null || !Directory.Exists(install))
                return null;

            var removed = 0;
            foreach (var staging in Directory.GetDirectories(install, StagingPrefix + "*"))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(staging) < StagingAge)
                    continue;

                if (DeleteFolder(staging))
                    removed++;
            }

            return removed switch
            {
                0 => null,
                1 => $"Self-update: one folder of an update that did not finish was removed from {install}.",
                _ => $"Self-update: {removed} folders of updates that did not finish were removed from {install}.",
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The build this program belongs to, a single program file or the folder that holds it, and
    /// the folder an update changes. The folder is null when the program has none of its own.
    /// </summary>
    private static (string Path, string? Folder) InstalledBuild(string running)
        => MacOsBundle.ForProgram(running) is { } bundle
            ? (bundle.BundlePath, bundle.Folder)
            : (running, Path.GetDirectoryName(running));

    /// <summary>Why the named build is not the one this build took the place of, or null when it is that build.</summary>
    private static string? Refusal(string previous, string? running, SelfUpdateHandover handover)
    {
        if (running is null)
            return "this process has no program file.";

        running = Path.GetFullPath(running);
        var build = InstalledBuild(running);
        if (!string.Equals(previous, ReplacedPath(build.Path), PathComparison))
            return "it is not the build this build took the place of.";

        var folder = build.Folder;
        if (folder is null)
            return "this build has no folder of its own.";

        if (!SelfUpdateReceipt.Matches(folder, handover, previous))
            return "this build carries no receipt for it.";

        return null;
    }

    /// <summary>Waits for the replaced process, and not at all when another program took its number.</summary>
    private static void WaitForExit(int processId, string programName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (string.Equals(process.ProcessName, programName, StringComparison.OrdinalIgnoreCase))
                process.WaitForExit((int)Wait.TotalMilliseconds);
        }
        catch (SystemException)
        {
            // the process has already ended, which is what the wait is for
        }
    }

    private static string Remove(string previous)
        => Delete(previous)
            ? $"Self-update: the replaced build at {previous} was removed."
            : $"Self-update: the replaced build at {previous} is still in use, so it stays until the next update.";

    /// <summary>Deletes one folder with everything in it, and says whether it is gone.</summary>
    private static bool DeleteFolder(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes one build, a program file or a folder with everything in it, and tries again for a
    /// while because Windows keeps a program file open until the last handle of the ended process is gone.
    /// </summary>
    private static bool Delete(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else
                    File.Delete(path);

                return true;
            }
            catch (DirectoryNotFoundException)
            {
                // the folder is gone, so the file is gone with it
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= DeleteAttempts)
                    return false;

                Thread.Sleep(RetryDelay);
            }
        }
    }
}
