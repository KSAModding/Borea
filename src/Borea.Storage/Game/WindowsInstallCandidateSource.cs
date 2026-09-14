using System.Runtime.Versioning;
using System.Security;
using Borea.Core.Game;
using Microsoft.Win32;

namespace Borea.Storage.Game;

/// <summary>
/// Reads the uninstall entries of the registry and the folders where users
/// unpack StarMap. It never writes to the registry.
/// </summary>
public sealed class WindowsInstallCandidateSource : IInstallCandidateSource
{
    internal const string GameUninstallKeyName = "{BA65F5AF-CD20-48A4-A957-410CB9660FF7}_is1";

    internal const string LoaderDisplayNamePrefix = "StarMap version";

    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly Func<IReadOnlyList<UninstallEntry>> _uninstallEntries;
    private readonly IReadOnlyList<string> _loaderFolders;
    private readonly string? _modsFolder;

    [SupportedOSPlatform("windows")]
    public WindowsInstallCandidateSource()
        : this(
            ReadUninstallEntries,
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "StarMap"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "StarMap"),
            ],
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Kitten Space Agency", "mods"))
    {
    }

    internal WindowsInstallCandidateSource(
        Func<IReadOnlyList<UninstallEntry>> uninstallEntries,
        IReadOnlyList<string> loaderFolders,
        string? modsFolder)
    {
        _uninstallEntries = uninstallEntries ?? throw new ArgumentNullException(nameof(uninstallEntries));
        _loaderFolders = loaderFolders ?? throw new ArgumentNullException(nameof(loaderFolders));
        _modsFolder = modsFolder;
    }

    public IReadOnlyList<string> GetGameDirectories() =>
        _uninstallEntries()
            .Where(entry => string.Equals(entry.KeyName, GameUninstallKeyName, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.InstallLocation)
            .OfType<string>()
            .ToList();

    public IReadOnlyList<string> GetLoaderDirectories()
    {
        var directories = _uninstallEntries()
            .Where(entry => entry.DisplayName?.StartsWith(LoaderDisplayNamePrefix, StringComparison.OrdinalIgnoreCase) == true)
            .Select(entry => entry.InstallLocation)
            .OfType<string>()
            .Concat(_loaderFolders)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_modsFolder))
        {
            try
            {
                directories.AddRange(Directory.EnumerateDirectories(_modsFolder));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // an unreadable mods folder only offers fewer candidates
            }
        }

        return directories;
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<UninstallEntry> ReadUninstallEntries()
    {
        var entries = new List<UninstallEntry>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(UninstallKeyPath, writable: false);
                    if (uninstall is null)
                        continue;

                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = uninstall.OpenSubKey(name, writable: false);
                            if (key is not null)
                                entries.Add(new UninstallEntry(name, key.GetValue("DisplayName") as string, key.GetValue("InstallLocation") as string));
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                        {
                            // an entry Borea may not read offers no candidate
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                {
                    // a hive Borea may not read offers no candidates
                }
            }
        }

        return entries;
    }
}

internal sealed record UninstallEntry(string KeyName, string? DisplayName, string? InstallLocation);
