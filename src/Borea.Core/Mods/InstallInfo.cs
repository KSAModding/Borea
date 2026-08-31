namespace Borea.Core.Mods;

/// <summary>
/// The install directive stamped into a release file (RFC 0031, extended by RFC 0035).
/// </summary>
public sealed class InstallInfo
{
    /// <summary>
    /// The directory inside the archive that becomes the installed content.
    /// Null means the archive root, never "unknown".
    /// </summary>
    public string? Root { get; }

    /// <summary>True when tooling derived the root from the standard archive layout.</summary>
    public bool Derived { get; }

    /// <summary>
    /// The anchor the content is written to. Null means the type default: a mod
    /// goes into the mods folder, a mod loader is undescribed.
    /// </summary>
    public InstallAnchor? Target { get; }

    /// <summary>The path below the anchor. Null means the anchor itself.</summary>
    public string? Path { get; }

    public InstallInfo(string? root, bool derived, InstallAnchor? target = null, string? path = null)
    {
        Root = RelativePaths.Contained(root, nameof(root));
        Derived = derived;
        Target = target;
        Path = RelativePaths.Contained(path, nameof(path));
    }
}
