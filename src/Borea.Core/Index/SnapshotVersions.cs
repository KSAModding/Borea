namespace Borea.Core.Index;

public static class SnapshotVersions
{
    /// <summary>
    /// The highest snapshot version this build reads.
    /// </summary>
    public const int Highest = 1;

    /// <summary>
    /// Whether the snapshot is from a newer format than this build implements.
    /// Such a snapshot is refused whole and the cached copy is kept.
    /// </summary>
    public static bool IsAboveHighest(int snapshotVersion) => snapshotVersion > Highest;
}
