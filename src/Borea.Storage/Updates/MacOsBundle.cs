namespace Borea.Storage.Updates;

/// <summary>
/// The macOS app bundle a program runs in, read from its path alone. A bundle is a folder named
/// <c>Name.app</c> with the program under <c>Contents/MacOS</c>. macOS signs the bundle as one
/// piece, so an update replaces the whole bundle and writes nothing inside it.
/// </summary>
public sealed class MacOsBundle
{
    private const string ContentsFolderName = "Contents";

    private const string ProgramsFolderName = "MacOS";

    private const string Extension = ".app";

    private MacOsBundle(string programPath, string bundlePath, string folder)
    {
        ProgramPath = programPath;
        BundlePath = bundlePath;
        Folder = folder;
    }

    /// <summary>The program that runs, inside the bundle.</summary>
    public string ProgramPath { get; }

    /// <summary>The bundle, such as /Applications/Borea.app.</summary>
    public string BundlePath { get; }

    /// <summary>The folder that holds the bundle, where an update unpacks the new one.</summary>
    public string Folder { get; }

    /// <summary>Where the bundle is moved to while a new one takes its place.</summary>
    public string ReplacedPath => SelfUpdateCleanup.ReplacedPath(BundlePath);

    /// <summary>The bundle that <paramref name="programPath"/> lies in, or null when it lies in none.</summary>
    public static MacOsBundle? ForProgram(string? programPath)
    {
        if (string.IsNullOrEmpty(programPath))
            return null;

        var program = Path.GetFullPath(programPath);
        var programs = Path.GetDirectoryName(program);
        var contents = programs is null ? null : Path.GetDirectoryName(programs);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);
        var folder = bundle is null ? null : Path.GetDirectoryName(bundle);
        if (programs is null || contents is null || bundle is null || folder is null)
            return null;

        if (!IsNamed(programs, ProgramsFolderName) || !IsNamed(contents, ContentsFolderName) || !IsBundle(bundle))
            return null;

        return new MacOsBundle(program, bundle, folder);
    }

    /// <summary>The same program inside another copy of the bundle, such as the one an update unpacked.</summary>
    public string ProgramIn(string bundlePath)
        => Path.Combine(bundlePath, ContentsFolderName, ProgramsFolderName, Path.GetFileName(ProgramPath));

    /// <summary>Whether the folder carries the bundle extension, which macOS reads in any case.</summary>
    public static bool IsBundle(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length > Extension.Length && name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamed(string path, string name)
        => string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal);
}
