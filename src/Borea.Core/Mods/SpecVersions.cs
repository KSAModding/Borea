namespace Borea.Core.Mods;

public static class SpecVersions
{
    /// <summary>
    /// The highest spec version this build reads, and the one it stamps on
    /// documents it synthesizes itself.
    /// </summary>
    public const int Highest = 1;

    /// <summary>
    /// Whether the document is from a newer format than this build implements.
    /// </summary>
    public static bool IsAboveHighest(int specVersion) => specVersion > Highest;
}
