using System.Buffers;

namespace Borea.Core.Game;

/// <summary>
/// The drives of a Wine prefix, each a drive letter over a host folder. It
/// maps paths between the host and the programs in the prefix the way Wine
/// does, and it reads no files.
/// </summary>
public sealed class WineDriveMap
{
    private static readonly SearchValues<char> NotInWindowsNames = SearchValues.Create(
        "<>:\"/\\|?*" + new string(Enumerable.Range(0, 32).Select(code => (char)code).ToArray()));

    private readonly SortedDictionary<char, HostPath> _roots = [];
    private readonly StringComparison _hostComparison;
    private readonly char _hostSeparator;

    /// <param name="drives">Each drive letter with the absolute host folder it stands for, with its links resolved.</param>
    /// <param name="hostComparison">How the host compares paths: ordinal on Linux, ordinal ignoring case on macOS.</param>
    /// <param name="hostSeparator">
    /// The separator of host paths. A backslash names a Windows host with drive
    /// letter roots, which only tests use, because Wine does not run there.
    /// </param>
    public WineDriveMap(IReadOnlyDictionary<char, string> drives, StringComparison hostComparison, char hostSeparator = '/')
    {
        ArgumentNullException.ThrowIfNull(drives);
        if (hostSeparator is not ('/' or '\\'))
            throw new ArgumentOutOfRangeException(nameof(hostSeparator), hostSeparator, "A host separates paths with '/' or '\\'.");

        _hostComparison = hostComparison;
        _hostSeparator = hostSeparator;
        foreach (var (letter, root) in drives)
        {
            if (!char.IsAsciiLetter(letter))
                throw new ArgumentException($"'{letter}' is not a drive letter.", nameof(drives));

            if (!TrySplitHost(root, out var path))
                throw new ArgumentException($"The root of drive {letter}: must be an absolute host path.", nameof(drives));

            _roots[char.ToLowerInvariant(letter)] = path;
        }

        Drives = _roots.ToDictionary(pair => pair.Key, pair => Join(pair.Value, []));
    }

    /// <summary>The host folder of each drive, by its lowercase letter.</summary>
    public IReadOnlyDictionary<char, string> Drives { get; }

    public static bool IsDrivePath(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';

    /// <summary>
    /// The Windows path of <paramref name="hostPath"/>, through the drive with
    /// the deepest root that holds it and the lowest letter among equally deep
    /// ones, as Wine's find_drive_nt_root chooses. Null when no drive holds the
    /// path, or when a part below the drive root holds a character Windows does
    /// not allow in a name.
    /// </summary>
    public string? ToWindows(string hostPath)
    {
        if (!TrySplitHost(hostPath, out var path))
            return null;

        char? drive = null;
        var depth = -1;
        foreach (var (letter, root) in _roots)
        {
            if (root.Parts.Length > depth && Holds(root, path))
            {
                drive = letter;
                depth = root.Parts.Length;
            }
        }

        if (drive is not { } found)
            return null;

        var rest = path.Parts[depth..];
        if (rest.Any(part => part.AsSpan().ContainsAny(NotInWindowsNames)))
            return null;

        return char.ToUpperInvariant(found) + @":\" + string.Join('\\', rest);
    }

    /// <summary>
    /// The host path of an absolute Windows path on one of the drives. Null for
    /// a relative path, a UNC or device path, a drive that is not in the map, a
    /// '..' part, or a part that holds a character Windows does not allow.
    /// </summary>
    public string? ToHost(string windowsPath)
    {
        if (!IsDrivePath(windowsPath) || !_roots.TryGetValue(char.ToLowerInvariant(windowsPath[0]), out var root))
            return null;

        var parts = new List<string>();
        foreach (var part in windowsPath[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
                return null;

            if (part == ".")
                continue;

            if (part.AsSpan().ContainsAny(NotInWindowsNames))
                return null;

            parts.Add(part);
        }

        return Join(root, parts);
    }

    private bool Holds(HostPath root, HostPath path)
    {
        if (!string.Equals(root.Root, path.Root, _hostComparison) || root.Parts.Length > path.Parts.Length)
            return false;

        for (var i = 0; i < root.Parts.Length; i++)
        {
            if (!string.Equals(root.Parts[i], path.Parts[i], _hostComparison))
                return false;
        }

        return true;
    }

    private string Join(HostPath root, IReadOnlyList<string> parts) =>
        root.Root + _hostSeparator + string.Join(_hostSeparator, root.Parts.Concat(parts));

    /// <summary>
    /// Splits an absolute host path into its root and its parts. A '..' part
    /// fails, because a path Borea maps is already resolved.
    /// </summary>
    private bool TrySplitHost(string? hostPath, out HostPath path)
    {
        path = default;
        if (string.IsNullOrEmpty(hostPath))
            return false;

        string root;
        if (_hostSeparator == '/')
        {
            if (hostPath[0] != '/')
                return false;

            root = string.Empty;
        }
        else
        {
            if (!IsDrivePath(hostPath) || hostPath[2] != '\\')
                return false;

            root = hostPath[..2];
        }

        var parts = hostPath[(root.Length + 1)..]
            .Split(_hostSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != ".")
            .ToArray();
        if (parts.Contains(".."))
            return false;

        path = new HostPath(root, parts);
        return true;
    }

    private readonly record struct HostPath(string Root, string[] Parts);
}
