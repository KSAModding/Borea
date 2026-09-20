namespace Borea.Core.Instances;

/// <summary>
/// A save or vehicle in the Backups folder of an instance. <see cref="Kind"/> and
/// <see cref="FolderName"/> are null when Borea cannot tell where it came from.
/// </summary>
/// <param name="Id">The path below the Backups folder of the instance, with "/" between the parts.</param>
/// <param name="FolderName">The name of the folder it came from in the saves or Vehicles folder.</param>
/// <param name="IsArchive">A zip that Back up wrote, and not a moved folder.</param>
public sealed record GameSaveBackup(
    Guid InstanceId,
    string Id,
    string Path,
    GameSaveKind? Kind,
    string? FolderName,
    string Name,
    DateTimeOffset CreatedAt,
    GameSaveBackupReason Reason,
    bool IsArchive,
    long SizeBytes)
{
    public bool CanRestore => Kind is not null && FolderName is not null;
}

public enum GameSaveBackupReason
{
    Unknown,
    BackedUp,
    Deleted,
    Replaced,
}
