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
    /// The version the release workflow stamped, with the commit after the
    /// "+". A local build reports 1.0.0 plus the commit it was built from.
    /// </summary>
    public static string BoreaInformationalVersion { get; } = ReadVersion();

    /// <summary>
    /// <see cref="BoreaInformationalVersion"/> without the build metadata, for the page.
    /// </summary>
    public static string BoreaVersion { get; } = BoreaInformationalVersion.Split('+')[0];

    public static string RuntimeText { get; } = RuntimeInformation.FrameworkDescription;

    public static string SystemText { get; } = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>
    /// The projects Borea is built on, as credits. The full list with every
    /// license text is the SBOM of a release; this names the ones a user meets.
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

    /// <summary>
    /// Where Borea keeps its settings, instances and loaders. Null without services.
    /// </summary>
    public string? BoreaFolder => _services is null ? null : Path.GetDirectoryName(_services.Paths.GetBoreaSettingsPath());

    public string? InstancesFolder => _services?.Paths.GetInstancesRoot();

    /// <summary>
    /// <see cref="BoreaFolder"/> as the page shows it, with the user profile
    /// shortened to "~" so a screenshot does not carry the user name. The
    /// copy button puts the real path on the clipboard.
    /// </summary>
    public string? BoreaFolderText => BoreaFolder is null ? null : WithoutUserProfile(BoreaFolder);

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

            if (_services is not null)
            {
                // the matched release is what the Game tab shows; the file version is the fallback for an adopted loader
                foreach (var (loaderId, installation) in _services.Settings.LoaderInstallations)
                {
                    var version = installation.Version?.ToString() ?? installation.RawVersion ?? "version unknown";
                    text.AppendLine($"{loaderId}: {version} at {WithoutUserProfile(installation.DirectoryPath)}");
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
    /// Called by the view after it put <see cref="DiagnosticsText"/> or
    /// <see cref="BoreaFolder"/> on the clipboard.
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
            else if (Path.IsPathRooted(target))
                AboutError = Localization.FormatAboutFolderMissing(target);
            else
                AboutError = Localization.FormatAboutCannotOpen(target);
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
            return informational;

        return typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// A path with the user profile folder replaced by "~", so a bug report
    /// does not carry the user name. Other paths are returned as they are.
    /// </summary>
    internal static string WithoutUserProfile(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0)
            return path;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.Equals(profile, comparison))
            return "~";

        var prefix = profile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison) ? "~" + Path.DirectorySeparatorChar + path[prefix.Length..] : path;
    }
}

public sealed record Credit(string Name, string Url);
