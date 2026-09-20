namespace Borea.Storage.Instances;

/// <summary>What the name of a backup does not tell. The instance and the kind come from the folders it is in.</summary>
public sealed class GameSaveBackupRecordDto
{
    /// <summary>The name of the folder in the saves or Vehicles folder.</summary>
    public string? Folder { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>"backed-up", "deleted" or "replaced". Null or an unknown name reads as unknown.</summary>
    public string? Reason { get; set; }
}
