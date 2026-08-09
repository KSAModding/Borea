namespace Borea.Core.Mods;

/// <summary>
/// Which directory inside the archive becomes the installed mod folder.
/// </summary>
public sealed class InstallInfo
{
    /// <summary>
    /// The directory inside the archive whose contents become the installed folder.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// True when tooling derived the root from the standard archive layout.
    /// </summary>
    public bool Derived { get; }

    public InstallInfo(string root, bool derived)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Install root cannot be empty.", nameof(root));

        // Platform-independent on purpose: the archive is built and consumed
        // on different systems, so Windows-rooted forms are rejected everywhere.
        if (root.StartsWith('/') || root.StartsWith('\\') || root.Contains(':') || root.Split('/', '\\').Any(s => s == ".."))
            throw new ArgumentException("Install root must be a relative path inside the archive.", nameof(root));

        Root = root;
        Derived = derived;
    }
}
