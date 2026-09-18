namespace Borea.Core.Settings;

public enum LibraryMoveStage
{
    Copying,

    /// <summary>The copy matched, the setting is saved, and the old files are deleted.</summary>
    RemovingOldFiles,
}

/// <summary>
/// Where a move to another volume stands. A move on one volume renames the
/// folders and reports nothing.
/// </summary>
public readonly record struct LibraryMoveProgress(LibraryMoveStage Stage, long Bytes, long TotalBytes, int Files, int TotalFiles)
{
    public double PercentComplete => TotalBytes > 0 ? (double)Bytes / TotalBytes * 100 : TotalFiles > 0 ? (double)Files / TotalFiles * 100 : 100;
}
