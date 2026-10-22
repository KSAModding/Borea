using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Borea.Core.ModLoaders;
using Borea.Core.Paths;
using Borea.Core.Updates;
using Borea.Storage.ModLoaders;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The About section of the settings modal: what is installed and whose work
/// ships inside, with the open and copy commands the Help section shares.
/// </summary>
public partial class MainViewModel
{
    public const string RepositoryUrl = "https://github.com/KSAModding/Borea";

    public const string ReportBugUrl = RepositoryUrl + "/issues/new";

    public const string ReleasesUrl = RepositoryUrl + "/releases";

    public const string CommunityUrl = "https://github.com/KSAModding";

    public const string DiscordUrl = "https://discord.gg/nt4fK4QuTz";

    /// <summary>
    /// The version the release workflow stamped, with the commit after the
    /// "+". A local build reports 1.0.0 plus the commit it was built from.
    /// </summary>
    public static string BoreaInformationalVersion => BoreaBuild.InformationalVersion;

    /// <summary>
    /// <see cref="BoreaInformationalVersion"/> without the build metadata, for the page.
    /// </summary>
    public static string BoreaVersion => BoreaBuild.Version;

    public static string RuntimeText { get; } = RuntimeInformation.FrameworkDescription;

    public static string SystemText { get; } = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>
    /// The projects Borea is built on, as credits. The SBOM of a release lists
    /// every package with its license; this names the ones a user meets.
    /// </summary>
    public static IReadOnlyList<Credit> Credits { get; } =
    [
        new(".NET", "https://dotnet.microsoft.com"),
        new("Avalonia", "https://avaloniaui.net"),
        new("SkiaSharp", "https://github.com/mono/SkiaSharp"),
        new("CommunityToolkit.Mvvm", "https://github.com/CommunityToolkit/dotnet"),
        new("Tomlyn", "https://github.com/xoofx/Tomlyn"),
        new("IBM Plex", "https://github.com/IBM/plex"),
        new("Phosphor Icons", "https://phosphoricons.com"),
    ];

    [ObservableProperty]
    private string? _aboutMessage;

    [ObservableProperty]
    private string? _aboutError;

    /// <summary>The line under the folder buttons, after the path was copied.</summary>
    [ObservableProperty]
    private string? _folderMessage;

    /// <summary>
    /// Where Borea keeps its settings, caches, logs and loaders. Null without services.
    /// </summary>
    public string? BoreaFolder => _services is null ? null : Path.GetDirectoryName(_services.Paths.GetBoreaSettingsPath());

    public string? InstancesFolder => _services?.Paths.GetInstancesRoot();

    public string? BoreaLogFile => _services?.Log.CurrentFilePath;

    public string? LogsFolder => _services?.Paths.GetLogsFolder();

    /// <summary>
    /// <see cref="BoreaFolder"/> as the page shows it, with the user profile
    /// shortened to "~" so a screenshot does not carry the user name. The
    /// copy button puts the real path on the clipboard.
    /// </summary>
    public string? BoreaFolderText => BoreaFolder is null ? null : WithoutUserProfile(BoreaFolder);

    /// <summary>
    /// The translation files whose texts show over the built ones, one per line, or null when there are none.
    /// </summary>
    public string? TranslationFilesText => Localization.TranslationFiles.Paths.Count == 0
        ? null
        : string.Join(Environment.NewLine, Localization.TranslationFiles.Paths.Select(WithoutUserProfile));

    /// <summary>
    /// The lines a bug report needs, ready for the clipboard.
    /// </summary>
    public string DiagnosticsText
    {
        get
        {
            var text = new StringBuilder()
                .AppendLine($"Borea {BoreaInformationalVersion}")
                .AppendLine(RuntimeText)
                .AppendLine(SystemText)
                .AppendLine($"KSA: {InstalledVersionText ?? "not set up"}");

            foreach (var path in Localization.TranslationFiles.Paths)
                text.AppendLine($"Translation file: {WithoutUserProfile(path)}");

            if (_services is not null)
            {
                if (_services.Paths.GetGameDirectoryPath() is { } gameDirectory)
                    text.AppendLine($"Game folder: {WithoutUserProfile(gameDirectory)}");

                text.AppendLine(IndexDiagnosticsLine(_services.IndexRefresh.Status));
                text.AppendLine($"Library: {WithoutUserProfile(LibraryFolder!)}");
                if (ActiveInstance is { } instance)
                    text.AppendLine($"Instance: {instance.Name}");

                // one line per installed version, and the record alone until the versions were listed
                var versions = _listedLoaderVersions ?? _services.Settings.LoaderInstallations
                    .Select(entry => new InstalledLoaderVersion(entry.Key, entry.Value.Version, entry.Value.RawVersion, entry.Value.DirectoryPath, LoaderLayoutMigration.IsPlayerFolder(_services.Paths.GetLoadersRoot(), entry.Value.DirectoryPath, entry.Value.IsAdopted), IsRecorded: true))
                    .ToList();
                foreach (var installed in versions)
                {
                    // the file version is the fallback for an adopted loader
                    var version = installed.Version?.ToString() ?? installed.RawVersion ?? "version unknown";
                    var marks = (installed.IsRecorded ? " (recorded)" : string.Empty) + (installed.IsAdopted ? " (adopted)" : string.Empty);
                    text.AppendLine($"{installed.LoaderId}: {version} at {WithoutUserProfile(installed.DirectoryPath)}{marks}");
                }

                text.AppendLine($"Log: {WithoutUserProfile(_services.Log.CurrentFilePath)}");
            }

            return text.ToString().TrimEnd();
        }
    }

    public const int DiagnosticsLogLines = 200;

    /// <summary>
    /// <see cref="DiagnosticsText"/> followed by the end of today's log, for the clipboard.
    /// </summary>
    internal string DiagnosticsWithLogText()
    {
        if (_services is null)
            return DiagnosticsText;

        var lines = _services.Log.ReadRecentLines(DiagnosticsLogLines);
        if (lines.Count == 0)
            return DiagnosticsText;

        return new StringBuilder(DiagnosticsText)
            .AppendLine()
            .AppendLine()
            .AppendLine("End of today's log:")
            .AppendJoin(Environment.NewLine, lines)
            .ToString();
    }

    [RelayCommand]
    private async Task ShowAboutSettingsAsync()
    {
        AboutMessage = null;
        AboutError = null;
        FolderMessage = null;
        NoticesMessage = null;
        NoticesError = null;
        UpdateIndexRefreshStatus();
        SettingsTab = SettingsTab.About;

        // the diagnostics name every installed loader version
        if (_services is not { } services)
            return;

        try
        {
            _listedLoaderVersions = await services.LoaderVersions.ListAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // the diagnostics keep the versions of the last list, or the record
        }
    }

    [RelayCommand]
    private void OpenBoreaFolder() => OpenFromAbout(BoreaFolder);

    [RelayCommand]
    private void OpenInstancesFolder() => OpenFromAbout(InstancesFolder);

    [RelayCommand]
    private void OpenBoreaLog() => OpenFromAbout(BoreaLogFile);

    [RelayCommand]
    private void OpenLogFolder() => OpenFromAbout(LogsFolder);

    [RelayCommand]
    private void OpenAboutLink(string? url) => OpenFromAbout(url);

    /// <summary>
    /// Called by the view after it put <see cref="DiagnosticsWithLogText"/> on the clipboard.
    /// </summary>
    internal void ReportDiagnosticsCopied()
    {
        AboutError = null;
        AboutMessage = Localization.AboutCopied;
    }

    /// <summary>
    /// Called by the view after it put <see cref="BoreaFolder"/> on the clipboard.
    /// </summary>
    internal void ReportFolderCopied()
    {
        AboutError = null;
        FolderMessage = Localization.AboutCopied;
    }

    /// <summary>Starts a folder or a URL through the system. Tests replace it.</summary>
    internal Action<string> OpenWithSystem { get; set; } = target => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    private void OpenFromAbout(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        AboutMessage = null;
        FolderMessage = null;
        var error = TryOpenWithSystem(target);
        if (error is not null)
            AboutError = error;
    }

    /// <summary>Opens an existing folder, file or URL, and returns why it could not, or null.</summary>
    internal string? TryOpenWithSystem(string target)
    {
        try
        {
            if (Directory.Exists(target) || File.Exists(target) || Uri.IsWellFormedUriString(target, UriKind.Absolute))
            {
                OpenWithSystem(target);
                return null;
            }

            return Path.IsPathRooted(target)
                ? Localization.FormatAboutFolderMissing(target)
                : Localization.FormatAboutCannotOpen(target);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// A path with the user profile folder replaced by "~", so a bug report
    /// does not carry the user name. Other paths are returned as they are.
    /// </summary>
    internal static string WithoutUserProfile(string path) => UserProfilePaths.Hide(path);
}

public sealed record Credit(string Name, string Url);
