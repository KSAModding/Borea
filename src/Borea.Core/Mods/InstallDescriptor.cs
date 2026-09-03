using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// The authored [install] table of RFC 0035. <see cref="InstallInfo"/> is the stamped half.
/// </summary>
public sealed class InstallDescriptor
{
    /// <summary>The directory inside the archive. Null means derived.</summary>
    public string? Root { get; }

    /// <summary>Null means the type default, Unknown means a manager must not guess.</summary>
    public InstallAnchor? Target { get; }

    /// <summary>The path below the anchor. Null means the anchor itself.</summary>
    public string? Path { get; }

    /// <summary>Paths the content owns and a manager must not edit.</summary>
    public IReadOnlyList<string>? Manages { get; }

    /// <summary>
    /// Prose, never parsed for actions. Null means the author said nothing,
    /// empty means they said there is none.
    /// </summary>
    public IReadOnlyList<string>? Steps { get; }

    /// <summary>How to remove the content cleanly. Prose, like Steps.</summary>
    public IReadOnlyList<string>? Uninstall { get; }

    public InstallDescriptor(
        string? root = null,
        InstallAnchor? target = null,
        string? path = null,
        IReadOnlyList<string>? manages = null,
        IReadOnlyList<string>? steps = null,
        IReadOnlyList<string>? uninstall = null)
    {
        Root = RelativePaths.Contained(root, nameof(root));
        Target = target;
        Path = RelativePaths.Contained(path, nameof(path));
        Manages = Copy(manages, nameof(manages));
        Steps = steps is null ? null : new ReadOnlyCollection<string>(steps.ToArray());
        Uninstall = uninstall is null ? null : new ReadOnlyCollection<string>(uninstall.ToArray());

        // ModManifest.Save regenerates it from the game's own list.
        if (Target == InstallAnchor.UserData && Path is null && Manages?.Any(IsGameManifest) == true)
            throw new ArgumentException("manifest.toml under user-data belongs to the game and cannot be managed.", nameof(manages));
    }

    private static bool IsGameManifest(string claimed) =>
        string.Equals(claimed.TrimStart('.', '/').TrimEnd('/'), "manifest.toml", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string>? Copy(IReadOnlyList<string>? manages, string paramName)
    {
        if (manages is null)
            return null;

        foreach (var claimed in manages)
            RelativePaths.Contained(claimed, paramName);

        return new ReadOnlyCollection<string>(manages.ToArray());
    }
}
