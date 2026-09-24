namespace Borea.Core.Mods;

/// <summary>How the files of a Borea-owned mod are held in its instance.</summary>
public enum ModStorage
{
    /// <summary>The instance has its own copy, marked with the ownership token.</summary>
    Private,

    /// <summary>The mod folder links to the one stored copy of the release that instances share.</summary>
    Linked,
}
