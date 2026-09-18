using Borea.Core.Game;

namespace Borea.Storage.Launch;

internal static class DotnetHost
{
    public static string? AssemblyBeside(string executable)
    {
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        var assembly = Path.ChangeExtension(executable, ".dll");
        return File.Exists(assembly) ? assembly : null;
    }

    /// <summary>The dotnet host of this system, or null.</summary>
    public static string? Find(OsPlatform? platform) => Find(
        platform,
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("DOTNET_ROOT"),
        DefaultDirectories(platform));

    /// <summary>
    /// The first dotnet file in the absolute directories of a PATH value, then
    /// in the DOTNET_ROOT directory, then in the default install directories.
    /// </summary>
    public static string? Find(OsPlatform? platform, string? searchPath, string? dotnetRoot, IEnumerable<string> defaultDirectories)
    {
        var fileName = platform == OsPlatform.Windows ? "dotnet.exe" : "dotnet";
        string[] pathDirectories = string.IsNullOrEmpty(searchPath)
            ? []
            : searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in pathDirectories.Append(dotnetRoot ?? string.Empty).Concat(defaultDirectories))
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
                continue;

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string[] DefaultDirectories(OsPlatform? platform) => platform switch
    {
        OsPlatform.Windows => [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")],
        OsPlatform.Linux => ["/usr/share/dotnet", "/usr/lib/dotnet", "/usr/lib64/dotnet", UserDotnet()],
        OsPlatform.MacOs => ["/usr/local/share/dotnet", UserDotnet()],
        _ => [],
    };

    private static string UserDotnet() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
}
