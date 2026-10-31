using System.Security;
using Borea.Core.Game;

namespace Borea.Storage.Game;

/// <summary>
/// Finds the game and StarMap in the Wine prefixes of macOS wrapper apps in
/// the application folders and in their direct subfolders, such as
/// ~/Applications/Sikarugir, where wrapper tools save their apps. It looks no
/// deeper and never inside an app. It reads the uninstall entries that
/// <see cref="WindowsInstallCandidateSource"/> reads on Windows from the
/// registry files of each prefix, maps their folders to the host, and writes
/// nothing.
/// </summary>
public sealed class WrapperInstallCandidateSource : IInstallCandidateSource
{
    private static readonly string[] UninstallKeyPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    // The machine hive and the hive of the prefix user, where a per-user installer writes.
    private static readonly string[] RegistryFiles = ["system.reg", "user.reg"];

    private static readonly string[] LoaderFolders = [@"C:\Program Files\StarMap", @"C:\Program Files (x86)\StarMap"];

    private readonly IReadOnlyList<string> _applicationFolders;
    private readonly IWinePrefixProbe _wine;

    public WrapperInstallCandidateSource()
        : this(
            ["/Applications", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications")],
            new WinePrefixProbe())
    {
    }

    internal WrapperInstallCandidateSource(IReadOnlyList<string> applicationFolders, IWinePrefixProbe wine)
    {
        _applicationFolders = applicationFolders ?? throw new ArgumentNullException(nameof(applicationFolders));
        _wine = wine ?? throw new ArgumentNullException(nameof(wine));
    }

    public IReadOnlyList<string> GetGameDirectories() => InPrefixes().GetGameDirectories();

    public IReadOnlyList<string> GetLoaderDirectories() => InPrefixes().GetLoaderDirectories();

    private WindowsInstallCandidateSource InPrefixes()
    {
        var entries = new List<UninstallEntry>();
        var loaderFolders = new List<string>();
        foreach (var prefix in Prefixes())
        {
            if (_wine.Find(prefix) is not { } install)
                continue;

            AddUninstallEntries(entries, install);

            loaderFolders.AddRange(LoaderFolders.Select(install.Drives.ToHost).OfType<string>());
        }

        return new WindowsInstallCandidateSource(() => entries, loaderFolders, modsFolder: null);
    }

    private static void AddUninstallEntries(List<UninstallEntry> entries, WineInstall install)
    {
        foreach (var file in RegistryFiles)
        {
            foreach (var keyPath in UninstallKeyPaths)
            {
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> keys;
                try
                {
                    keys = WineRegistryFile.ReadSubKeys(Path.Combine(install.PrefixRoot, file), keyPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                {
                    continue;
                }

                foreach (var (name, values) in keys)
                {
                    var location = values.GetValueOrDefault("InstallLocation") is { } windows ? install.Drives.ToHost(windows) : null;
                    entries.Add(new UninstallEntry(name, values.GetValueOrDefault("DisplayName"), location));
                }
            }
        }
    }

    private IEnumerable<string> Prefixes()
    {
        foreach (var folder in _applicationFolders)
        {
            foreach (var entry in Subfolders(folder))
            {
                var apps = IsApp(entry) ? [entry] : Subfolders(entry).Where(IsApp);
                foreach (var app in apps)
                {
                    var prefix = Path.Combine(app, "Contents", "SharedSupport", "prefix");
                    if (File.Exists(Path.Combine(prefix, "system.reg")))
                        yield return prefix;
                }
            }
        }
    }

    private static bool IsApp(string folder) => folder.EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    private static string[] Subfolders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // a folder Borea may not read offers no candidates
            return [];
        }
    }
}
