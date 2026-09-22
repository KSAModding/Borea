using System.Globalization;
using System.Text.RegularExpressions;
using Borea.Core.Instances;
using Borea.Core.Paths;
using Borea.Storage.Toml;

namespace Borea.Storage.Instances;

/// <summary>
/// The layout of the Backups folder: Backups/&lt;instance id&gt;/&lt;saves or Vehicles&gt;/&lt;folder&gt;-&lt;UTC time&gt;,
/// a moved folder or a zip, with a record next to it named like it plus ".borea.toml".
/// </summary>
internal sealed partial class GameSaveBackupFolder
{
    internal const string RecordSuffix = ".borea.toml";

    /// <summary>The start of a name that Borea is still writing or deleting.</summary>
    internal const string WorkPrefix = ".borea-";

    private const string StampFormat = "yyyy-MM-dd'T'HHmmss'Z'";

    private const string BackedUpName = "backed-up";

    private const string DeletedName = "deleted";

    private const string ReplacedName = "replaced";

    private readonly IGamePathProvider _paths;
    private readonly TimeProvider _time;

    public GameSaveBackupFolder(IGamePathProvider paths, TimeProvider time)
    {
        _paths = paths;
        _time = time;
    }

    public DateTimeOffset Now() => _time.GetUtcNow();

    public string InstanceFolder(Guid instanceId) => InstanceFolder(_paths, instanceId);

    /// <summary>The backup folder of one instance, for callers that have no reason to hold a TimeProvider.</summary>
    public static string InstanceFolder(IGamePathProvider paths, Guid instanceId)
        => Path.Combine(paths.GetBackupsRoot(), instanceId.ToString());

    public string KindFolder(Guid instanceId, GameSaveKind kind) => Path.Combine(InstanceFolder(instanceId), FolderName(kind));

    // GameSaves.SaveFolderPath and VehicleSaves.SaveFolderPath below Constants.DocumentsFolderPath
    public static string FolderName(GameSaveKind kind) => kind == GameSaveKind.Save ? "saves" : "Vehicles";

    public static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);

    public static string Name(string folderName, string stamp, int attempt)
        => attempt == 1 ? $"{folderName}-{stamp}" : $"{folderName}-{stamp}-{attempt}";

    public async Task<string> MoveInAsync(Guid instanceId, GameSaveKind kind, string folder, GameSaveBackupReason reason)
    {
        var backups = Directory.CreateDirectory(KindFolder(instanceId, kind)).FullName;
        var now = Now();
        var folderName = Path.GetFileName(folder);
        for (var attempt = 1; ; attempt++)
        {
            var target = Path.Combine(backups, Name(folderName, Stamp(now), attempt));
            if (Directory.Exists(target) || File.Exists(target))
                continue;

            Directory.Move(folder, target);
            await TryWriteRecordAsync(target, folderName, now, reason).ConfigureAwait(false);
            return target;
        }
    }

    /// <summary>The name of a backup tells its folder and time, so a record that cannot be written loses only the reason.</summary>
    public static async Task TryWriteRecordAsync(string backup, string folderName, DateTimeOffset createdAt, GameSaveBackupReason reason)
    {
        var record = new GameSaveBackupRecordDto
        {
            Folder = folderName,
            CreatedAt = createdAt,
            Reason = reason switch
            {
                GameSaveBackupReason.BackedUp => BackedUpName,
                GameSaveBackupReason.Deleted => DeletedName,
                GameSaveBackupReason.Replaced => ReplacedName,
                _ => null,
            },
        };

        try
        {
            await TomlFileStore.WriteAsync(backup + RecordSuffix, record).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static async Task<GameSaveBackupRecordDto?> TryReadRecordAsync(string backup, CancellationToken cancellationToken)
    {
        try
        {
            return await TomlFileStore.ReadAsync<GameSaveBackupRecordDto>(backup + RecordSuffix, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static void TryDeleteRecord(string backup)
    {
        try
        {
            File.Delete(backup + RecordSuffix);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static GameSaveBackupReason ReadReason(string? name) => name?.ToLowerInvariant() switch
    {
        BackedUpName => GameSaveBackupReason.BackedUp,
        DeletedName => GameSaveBackupReason.Deleted,
        ReplacedName => GameSaveBackupReason.Replaced,
        _ => GameSaveBackupReason.Unknown,
    };

    /// <summary>The folder and the time in a name that <see cref="Name"/> wrote, or null for any other name.</summary>
    public static (string FolderName, DateTimeOffset CreatedAt)? ParseName(string name)
    {
        var match = NamePattern().Match(name);
        if (!match.Success
            || !DateTimeOffset.TryParseExact(match.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt))
            return null;

        return (match.Groups["folder"].Value, createdAt);
    }

    /// <summary>A name that stays inside the saves or Vehicles folder.</summary>
    public static bool IsFolderName(string? name)
        => !string.IsNullOrWhiteSpace(name)
            && name is not "." and not ".."
            && name.IndexOfAny(['/', '\\']) < 0
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    [GeneratedRegex(@"^(?<folder>.+)-(?<stamp>\d{4}-\d{2}-\d{2}T\d{6}Z)(-\d+)?$")]
    private static partial Regex NamePattern();
}
