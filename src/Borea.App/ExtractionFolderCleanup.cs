using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Logging;

namespace Borea.App;

/// <summary>
/// Deletes the folders that the .NET single-file host extracted for other Borea builds. The host
/// (extractor_t::extraction_dir in dotnet/runtime) extracts to &lt;base&gt;/&lt;program name&gt;/&lt;bundle id&gt;
/// and never deletes a folder.
/// </summary>
internal sealed class ExtractionFolderCleanup
{
    private const string BaseFolderVariable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    private static readonly TimeSpan RecentlyWritten = TimeSpan.FromMinutes(10);

    private static readonly EnumerationOptions EveryEntry = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
    };

    private readonly IBoreaLog _log;
    private readonly Func<bool> _isOtherInstanceRunning;
    private readonly Action<string> _deleteFolder;

    public ExtractionFolderCleanup(IBoreaLog log, Func<bool> isOtherInstanceRunning, Action<string>? deleteFolder = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _isOtherInstanceRunning = isOtherInstanceRunning ?? throw new ArgumentNullException(nameof(isOtherInstanceRunning));
        _deleteFolder = deleteFolder ?? DeleteFolder;
    }

    /// <summary>Starts the cleanup on the thread pool when this process runs as a single-file bundle with extracted files.</summary>
    public static void StartForThisProcess(IBoreaLog log)
    {
        if (!string.IsNullOrEmpty(typeof(ExtractionFolderCleanup).Assembly.Location) || Environment.ProcessPath is not { } processPath)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                var programName = ProgramName(processPath, OperatingSystem.IsWindows());
                var applicationFolder = Path.Combine(BaseFolder(), programName);
                var ownFolder = FindOwnFolder(applicationFolder, AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string);
                new ExtractionFolderCleanup(log, () => IsOtherProcessRunning(programName)).Run(applicationFolder, ownFolder);
            }
            catch (Exception exception)
            {
                log.Write("Extraction folder cleanup failed.", exception);
            }
        });
    }

    /// <summary>Deletes the other build folders in the application folder, but nothing while another Borea runs, and returns how many it deleted.</summary>
    public int Run(string applicationFolder, string? ownFolder)
    {
        var application = new DirectoryInfo(Path.GetFullPath(applicationFolder));
        if (ownFolder is null || !IsChildOf(Path.GetFullPath(ownFolder), application.FullName) || !Directory.Exists(ownFolder))
            return 0;

        var own = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownFolder)));
        var removed = 0;
        long bytes = 0;
        try
        {
            if (application.LinkTarget is not null)
                return 0;

            foreach (var folder in application.EnumerateDirectories("*", EveryEntry))
            {
                // a host that is still extracting writes to its folder
                if (string.Equals(folder.Name, own, PathComparison) || IsLink(folder) || DateTime.UtcNow - folder.LastWriteTimeUtc < RecentlyWritten)
                    continue;

                // a failed delete does not show that a folder is in use, because Linux and macOS delete open files
                // and Windows deletes the files of a folder that another Borea has not loaded yet
                if (_isOtherInstanceRunning())
                    break;

                try
                {
                    var size = Size(folder);
                    _deleteFolder(folder.FullName);
                    removed++;
                    bytes += size;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        if (removed > 0)
            _log.Write($"Extraction folders of other Borea builds removed: {removed}, {bytes} bytes.");

        return removed;
    }

    /// <summary>The host names the application folder after the program file, without ".exe" on Windows.</summary>
    internal static string ProgramName(string processPath, bool windows)
    {
        var name = Path.GetFileName(processPath);
        return windows && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    internal static string? FindOwnFolder(string applicationFolder, string? nativeSearchFolders)
    {
        if (string.IsNullOrEmpty(nativeSearchFolders))
            return null;

        var application = Path.GetFullPath(applicationFolder);
        var candidates = nativeSearchFolders
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathFullyQualified)
            .Select(folder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))
            .Where(folder => IsChildOf(folder, application))
            .Distinct(StringComparer.FromComparison(PathComparison))
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // the default of the .NET host, pal::get_default_bundle_extraction_base_dir
    private static string BaseFolder()
    {
        var configured = Environment.GetEnvironmentVariable(BaseFolderVariable);
        if (!string.IsNullOrEmpty(configured))
            return Path.GetFullPath(configured);

        return OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), ".net")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".net");
    }

    private static bool IsChildOf(string folder, string parent)
    {
        var parentOfFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        return parentOfFolder is not null
            && string.Equals(parentOfFolder, Path.TrimEndingDirectorySeparator(parent), PathComparison);
    }

    private static bool IsLink(FileSystemInfo entry)
        => entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private static long Size(DirectoryInfo folder)
    {
        long size = 0;
        foreach (var entry in folder.EnumerateFileSystemInfos("*", EveryEntry))
        {
            if (IsLink(entry))
                continue;

            size += entry switch
            {
                FileInfo file => file.Length,
                DirectoryInfo directory => Size(directory),
                _ => 0,
            };
        }

        return size;
    }

    // Directory.Delete with recursive set throws on Windows after it removed a junction, so links are removed one by one here.
    private static void DeleteFolder(string folder)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", EveryEntry))
        {
            if (entry is DirectoryInfo directory && !IsLink(directory))
                DeleteFolder(directory.FullName);
            else if (entry is DirectoryInfo && OperatingSystem.IsWindows())
                Directory.Delete(entry.FullName);
            else
                File.Delete(entry.FullName);
        }

        Directory.Delete(folder);
    }

    private static bool IsOtherProcessRunning(string programName)
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(process => process.Id != Environment.ProcessId && RunsProgram(process, programName));
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static bool RunsProgram(Process process, string programName)
    {
        try
        {
            if (string.Equals(process.ProcessName, programName, StringComparison.OrdinalIgnoreCase))
                return true;

            // Linux names a process after the link that started it, but the host names the folder after the file behind the link
            return OperatingSystem.IsLinux()
                && string.Equals(Path.GetFileName(new FileInfo($"/proc/{process.Id}/exe").LinkTarget), programName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
