namespace Borea.Storage.Preview.Sources;

/// <summary>
/// The one form of a path inside a mod that every preview file source compares. The game builds an asset path with
/// Path.Combine from the folder of the declaring mod (Mod.GetPath, FileReference.ModPath), so on Windows it accepts
/// '\' and a name in any case. The sources therefore normalize both the requested path and the stored names the same
/// way before they look one up.
/// </summary>
internal static class PreviewPath
{
    /// <summary>
    /// Returns the path with '/' as the separator, without a leading "./", and without empty or "." segments. Returns
    /// null for a path that is empty, rooted, holds a drive or stream colon, holds a NUL character, or holds a ".."
    /// segment, because such a path could reach outside the mod folder or the archive root.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\0', StringComparison.Ordinal))
            return null;

        var slashed = path.Replace('\\', '/');
        if (slashed.StartsWith('/') || slashed.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(path))
            return null;

        var segments = new List<string>();
        foreach (var segment in slashed.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;

            if (segment == "..")
                return null;

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary>
    /// Whether the entry is a text file that gets the smaller cap, because an author XML or TOML file is read whole
    /// into a parser.
    /// </summary>
    public static bool IsText(string normalizedPath) =>
        normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        || normalizedPath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase);
}
