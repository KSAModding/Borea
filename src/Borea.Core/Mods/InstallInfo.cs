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
        Root = Contained(root, nameof(root));
        Derived = derived;
        Target = target;
        Path = Contained(path, nameof(path));
    }

    /// <summary>
    /// The value as a relative, '/' separated path that stays inside its anchor
    /// (RFC 0035 rules 1 and 2). Null passes through, empty does not. A backslash
    /// is rejected rather than read as a separator, the way the index stamps it.
    /// </summary>
    private static string? Contained(string? value, string paramName)
    {
        if (value is null)
            return null;

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A stated path cannot be empty, leave it absent instead.", paramName);

        if (value.StartsWith('/') || value.StartsWith('~') || value.Contains('\\') ||
            value.Contains(':') || value.Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException(
                "A path must be relative, '/' separated, and must stay inside its anchor.", paramName);
        }

        return value;
    }
}
