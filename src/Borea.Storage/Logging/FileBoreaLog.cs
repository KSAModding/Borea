using System.Globalization;
using System.Text;
using Borea.Core.Logging;
using Borea.Core.Paths;

namespace Borea.Storage.Logging;

/// <summary>
/// Appends lines to borea-yyyy-MM-dd.log in the logs folder, and on the first
/// line of a day deletes the files older than <see cref="KeptDays"/> days.
/// Every line opens the file shared, so the App and the CLI can both write.
/// </summary>
public sealed class FileBoreaLog : IBoreaLog
{
    public const int KeptDays = 7;

    private const string FilePrefix = "borea-";
    private const string FileExtension = ".log";
    private const string DateFormat = "yyyy-MM-dd";
    private const int MaxReadBytes = 64 * 1024;

    private readonly string _folder;
    private readonly string _source;
    private readonly TimeProvider _time;
    private readonly string _userProfile;
    private readonly object _gate = new();
    private DateOnly? _cleanedDay;

    /// <param name="userProfile">The folder written as "~". Null means the current user's profile.</param>
    public FileBoreaLog(IGamePathProvider paths, BoreaLogSource source, TimeProvider? time = null, string? userProfile = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _folder = paths.GetLogsFolder();
        _source = source switch
        {
            BoreaLogSource.App => "app",
            BoreaLogSource.Cli => "cli",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        _time = time ?? TimeProvider.System;
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string CurrentFilePath => FilePath(DateOnly.FromDateTime(_time.GetLocalNow().DateTime));

    public void Write(string message) => Append(message, exception: null);

    public void Write(string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Append(message, exception);
    }

    public IReadOnlyList<string> ReadRecentLines(int maxLines)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLines);

        try
        {
            using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - MaxReadBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: start == 0);

            var lines = reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            if (start > 0)
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            return lines.TakeLast(maxLines).ToList();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Append(string message, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(message);

        var now = _time.GetLocalNow();
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(_source).Append("] ")
            .Append(message);
        if (exception is not null)
            line.AppendLine().Append(exception);
        var bytes = Encoding.UTF8.GetBytes(UserProfilePaths.Hide(line.AppendLine().ToString(), _userProfile));

        var day = DateOnly.FromDateTime(now.DateTime);
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                if (_cleanedDay != day)
                {
                    DeleteOldFiles(day);
                    _cleanedDay = day;
                }

                using var stream = new FileStream(FilePath(day), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // the operation goes on without its line
            }
        }
    }

    private void DeleteOldFiles(DateOnly today)
    {
        foreach (var file in Directory.EnumerateFiles(_folder, FilePrefix + "*" + FileExtension))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateOnly.TryParseExact(name.AsSpan(FilePrefix.Length), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day > today.AddDays(-KeptDays))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // the next day tries again
            }
        }
    }

    private string FilePath(DateOnly day)
        => Path.Combine(_folder, FilePrefix + day.ToString(DateFormat, CultureInfo.InvariantCulture) + FileExtension);
}
