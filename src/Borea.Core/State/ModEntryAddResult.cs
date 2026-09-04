namespace Borea.Core.State;

/// <summary>
/// Outcome of <see cref="IModStateRepository.AddEntryAsync"/>. Not re-derivable
/// afterwards without reading the file again.
/// </summary>
public enum ModEntryAddResult
{
    /// <summary>The entry was written.</summary>
    Added = 0,

    /// <summary>Already listed, left as it was, enabled flag included.</summary>
    AlreadyListed = 1,

    /// <summary>The mod is not on disk, so nothing was written.</summary>
    NotOnDisk = 2,
}
