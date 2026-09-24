namespace Borea.Storage.Mods;

/// <summary>The snapshot file beside a stored release.</summary>
public sealed class StoredReleaseSnapshotDto
{
    public long Files { get; set; }

    public long Bytes { get; set; }

    public DateTimeOffset NewestWrite { get; set; }
}
