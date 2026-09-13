using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The About section of the settings modal: what is installed, where Borea
/// keeps its files, where to report a problem, and whose work ships inside.
/// </summary>
public partial class MainViewModel
{
    public const string RepositoryUrl = "https://github.com/KSAModding/Borea";

    public const string ReportBugUrl = RepositoryUrl + "/issues/new";

    public const string ReleasesUrl = RepositoryUrl + "/releases";

    public const string CommunityUrl = "https://github.com/KSAModding";

    public const string DiscordUrl = "https://discord.gg/nt4fK4QuTz";

    /// <summary>
    /// The version the release workflow stamped, without the build metadata
    /// after the "+". A local build reports 1.0.0.
    /// </summary>
    public static string BoreaVersion { get; } = ReadVersion();

    public static string RuntimeText { get; } = RuntimeInformation.FrameworkDescription;

    public static string SystemText { get; } = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>
    /// The work of others that ships inside Borea, with the license each one
    /// requires to be named.
    /// </summary>
    public static IReadOnlyList<ThirdPartyNotice> ThirdPartyNotices { get; } =
    [
        new("Avalonia", "MIT", "https://github.com/AvaloniaUI/Avalonia"),
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("IBM Plex", "SIL OFL 1.1", "https://github.com/IBM/plex"),
        new("Phosphor Icons", "MIT", "https://phosphoricons.com"),
    ];

    [ObservableProperty]
    private string? _aboutMessage;

    [ObservableProperty]
    private string? _aboutError;

    /// <summary>
    /// Where Borea keeps its settings, instances and loaders. Null without services.
    /// </summary>
    public string? BoreaFolder => _services is null ? null : Path.GetDirectoryName(_services.Paths.GetBoreaSettingsPath());

    public string? InstancesFolder => _services?.Paths.GetInstancesRoot();

    /// <summary>
    /// The lines a bug report needs, ready for the clipboard.
    /// </summary>
    public string DiagnosticsText
    {
        get
        {
            var text = new StringBuilder()
                .AppendLine($"Borea {BoreaVersion}")
                .AppendLine(RuntimeText)
                .AppendLine(SystemText)
                .AppendLine($"KSA: {InstalledVersionText ?? "not set up"}");

            if (_services is not null)
            {
                // the matched release is what the Game tab shows; the file version is the fallback for an adopted loader
                foreach (var (loaderId, installation) in _services.Settings.LoaderInstallations)
                {
                    var version = installation.Version?.ToString() ?? installation.RawVersion ?? "version unknown";
                    text.AppendLine($"{loaderId}: {version} at {installation.DirectoryPath}");
                }
            }

            return text.ToString().TrimEnd();
        }
    }

    [RelayCommand]
    private void ShowAboutSettings()
    {
        AboutMessage = null;
        AboutError = null;
        SettingsTab = SettingsTab.About;
    }

    [RelayCommand]
    private void OpenBoreaFolder() => OpenFromAbout(BoreaFolder);

    [RelayCommand]
    private void OpenInstancesFolder() => OpenFromAbout(InstancesFolder);

    [RelayCommand]
    private void OpenAboutLink(string? url) => OpenFromAbout(url);

    /// <summary>
    /// Called by the view after it put <see cref="DiagnosticsText"/> on the clipboard.
    /// </summary>
    internal void ReportDiagnosticsCopied()
    {
        AboutError = null;
        AboutMessage = Localization.AboutCopied;
    }

    /// <summary>
    /// Hands a folder or a URL to the system, the way the links on a content page open.
    /// </summary>
    private void OpenFromAbout(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        AboutMessage = null;
        try
        {
            if (Directory.Exists(target) || Uri.IsWellFormedUriString(target, UriKind.Absolute))
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else
                AboutError = Localization.FormatAboutFolderMissing(target);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            AboutError = exception.Message;
        }
    }

    private static string ReadVersion()
    {
        var informational = typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}

public sealed record ThirdPartyNotice(string Name, string License, string Url);
