namespace Borea.Core.Settings;

public enum LibraryFolderChangeOutcome
{
    /// <summary>The Instances and Backups folders are in the new folder, and the setting names it.</summary>
    Moved = 0,

    /// <summary>The new folder already held a library and the previous one held no instance, so nothing moved.</summary>
    Adopted = 1,

    NotAbsolute = 2,

    IsFile = 3,

    CurrentLibrary = 4,

    InsideCurrentLibrary = 5,

    ContainsCurrentLibrary = 6,

    /// <summary>The folder is inside Borea's own folder, which only the default library may use.</summary>
    InsideBoreaFolder = 7,

    ContainsBoreaFolder = 8,

    InsideGameDirectory = 9,

    InsideSharedProfile = 10,

    /// <summary>Borea could not create the folder or write a file into it.</summary>
    NotWritable = 11,

    /// <summary>The folder has an Instances or Backups folder with files but no instance Borea can read.</summary>
    TargetNotEmpty = 12,

    /// <summary>Both folders hold instances, and Borea does not merge libraries.</summary>
    BothHaveInstances = 13,

    /// <summary>Borea runs the game for an instance, or a KSA or StarMap process runs.</summary>
    GameRunning = 14,

    /// <summary>Another Borea App or command runs and would keep using the previous folder.</summary>
    BoreaRunning = 15,

    /// <summary>A change to an instance holds that instance's lock.</summary>
    InstanceBusy = 16,

    /// <summary>A file in the library cannot be opened.</summary>
    FileLocked = 17,
}

/// <param name="Folder">The chosen folder as a full path.</param>
/// <param name="PreviousFolder">The library folder before the change.</param>
/// <param name="Message">What happened, for the user.</param>
public sealed record LibraryFolderChangeResult(
    LibraryFolderChangeOutcome Outcome,
    string Folder,
    string PreviousFolder,
    string Message)
{
    /// <summary>The file that could not be opened when the outcome is <see cref="LibraryFolderChangeOutcome.FileLocked"/>.</summary>
    public string? LockedFile { get; init; }

    /// <summary>True when a copy succeeded but Borea could not delete every old file.</summary>
    public bool OldFilesRemain { get; init; }

    public bool Changed => Outcome is LibraryFolderChangeOutcome.Moved or LibraryFolderChangeOutcome.Adopted;
}
