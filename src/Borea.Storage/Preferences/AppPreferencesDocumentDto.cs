using System.Text.Json.Serialization;

namespace Borea.Storage.Preferences;

internal sealed class AppPreferencesDocumentDto
{
    public int FormatVersion { get; set; }

    public string? SelectedTheme { get; set; }

    public string? RegionalCulture { get; set; }

    public string? UiCulture { get; set; }

    /// <summary>Null in an older file, which reads as on.</summary>
    public bool? CheckForUpdatesAtStart { get; set; }

    /// <summary>"stable", "testing" or "dev". Null or an unknown name reads as stable.</summary>
    public string? UpdateChannel { get; set; }

    public bool? ForeignFolderDeletionConfirmed { get; set; }

    /// <summary>Null in an older file, which reads as on.</summary>
    public bool? LoadImagesFromAuthorHosts { get; set; }

    /// <summary>"active-instance" or "without-mod-loader". Null or an unknown name reads as the active instance.</summary>
    public string? HomeLaunch { get; set; }

    /// <summary>"popularity", "recently-updated" or "name". Null or an unknown name reads as popularity.</summary>
    public string? DiscoverSortOrder { get; set; }

    public bool? SharedProfileBannerDismissed { get; set; }

    /// <summary>
    /// Written by Borea before 0.2.0, when a closed update banner stayed closed.
    /// Accepted so that such a file loads, never acted on, and removed when the file is read.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DismissedBoreaRelease { get; set; }

    public int? DismissedGameRevision { get; set; }

    /// <summary>An ISO 8601 time such as "2026-09-18T12:00:00+00:00". Null or a time that does not parse reads as not recorded.</summary>
    public string? FirstStartedAt { get; set; }

    /// <summary>Null in an older file, which reads as on.</summary>
    public bool? FetchAnnouncements { get; set; }

    public List<string?>? DismissedAnnouncements { get; set; }

    /// <summary>Null in an older file, which reads as on.</summary>
    public bool? OpenBoreaLinks { get; set; }

    /// <summary>Null keeps every backup. A value below one day reads as null.</summary>
    public int? BackupRetentionDays { get; set; }

    /// <summary>The revision of the game build whose untested-build banner was closed. A value below zero reads as null.</summary>
    public int? DismissedUntestedGameRevision { get; set; }

    public List<CustomThemePreferenceDto?>? CustomThemes { get; set; }
}

internal sealed class CustomThemePreferenceDto
{
    public required string Name { get; set; }

    public required string MainColor { get; set; }

    public required string SecondaryColor { get; set; }

    public required string GlobalPanelsColor { get; set; }

    public required string TextColor { get; set; }
}
