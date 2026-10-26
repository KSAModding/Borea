using System.Security;
using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Storage.Game;

namespace Borea.Storage.Launch;

/// <summary>
/// The shared .NET runtimes of a Wine prefix, in the folders where the .NET
/// host of a Windows x64 program looks for them. It only reads, and it never
/// installs a runtime.
/// </summary>
internal static class WineDotnetRuntime
{
    private const long MaxRuntimeConfigBytes = 1024 * 1024;

    private const string DefaultRoot = @"C:\Program Files\dotnet";

    // The installer of Microsoft writes this key in the 32-bit view of the registry, which Wine keeps below Wow6432Node.
    private static readonly string[] InstalledVersionsKeys =
    [
        @"Software\dotnet\Setup\InstalledVersions",
        @"Software\Wow6432Node\dotnet\Setup\InstalledVersions",
    ];

    /// <summary>
    /// The runtime that the program at <paramref name="launchFile"/> needs and
    /// the prefix of <paramref name="install"/> does not have. Null when the
    /// prefix has one that fits, or when no runtimeconfig.json next to the
    /// program names one.
    /// </summary>
    /// <param name="launchFile">The host path of the program, whose runtimeconfig.json has the same name.</param>
    public static DotnetRuntimeNeed? Missing(WineInstall install, string launchFile)
    {
        var need = Need(launchFile);
        return need is null || need.IsMetBy(InstalledVersions(install)) ? null : need;
    }

    private static DotnetRuntimeNeed? Need(string launchFile)
    {
        var config = new FileInfo(Path.Combine(Path.GetDirectoryName(launchFile)!, Path.GetFileNameWithoutExtension(launchFile) + ".runtimeconfig.json"));
        try
        {
            return config.Exists && config.Length <= MaxRuntimeConfigBytes ? DotnetRuntimeNeed.Parse(File.ReadAllText(config.FullName)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // a file Borea cannot read refuses nothing, and the host of the loader says what it misses
            return null;
        }
    }

    private static IEnumerable<string> InstalledVersions(WineInstall install)
    {
        foreach (var root in Roots(install).Distinct())
        {
            var shared = Path.Combine(root, "shared", DotnetRuntimeNeed.FrameworkName);
            string[] versions;
            try
            {
                versions = Directory.Exists(shared) ? Directory.GetDirectories(shared) : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            foreach (var version in versions)
                yield return Path.GetFileName(version);
        }
    }

    /// <summary>The default folder, then each folder that system.reg names for x64, as host paths.</summary>
    private static IEnumerable<string> Roots(WineInstall install)
    {
        if (install.Drives.ToHost(DefaultRoot) is { } defaultRoot)
            yield return defaultRoot;

        foreach (var key in InstalledVersionsKeys)
        {
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> architectures;
            try
            {
                architectures = WineRegistryFile.ReadSubKeys(Path.Combine(install.PrefixRoot, "system.reg"), key);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                yield break;
            }

            if (architectures.GetValueOrDefault("x64")?.GetValueOrDefault("InstallLocation") is { } location && install.Drives.ToHost(location) is { } root)
                yield return root;
        }
    }
}
