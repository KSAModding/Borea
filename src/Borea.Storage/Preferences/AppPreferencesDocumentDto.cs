using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Storage.Preferences;

internal sealed class AppPreferencesDocumentDto
{
    public int FormatVersion { get; set; }

    public string? SelectedTheme { get; set; }

    public string? RegionalCulture { get; set; }

    public string? UiCulture { get; set; }

    public bool? MarkUntranslatedTexts { get; set; }

    /// <summary>Null in an older file, which reads as on.</summary>
    public bool? CheckForUpdatesAtStart { get; set; }

    /// <summary>"stable", "testing" or "dev". Null or an unknown name reads as stable.</summary>
    public string? UpdateChannel { get; set; }

    public bool? ForeignFolderDeletionConfirmed { get; set; }

    /// <summary>Null in an older file, which reads as on.</summary>
    public bool? LoadImagesFromAuthorHosts { get; set; }

    /// <summary>"active-instance" or "without-mod-loader". Null or an unknown name reads as the active instance.</summary>
    public string? HomeLaunch { get; set; }

    /// <summary>"popularity", "recently-updated", "recently-listed" or "name". Null or an unknown name reads as popularity.</summary>
    public string? DiscoverSortOrder { get; set; }

    public bool? SharedProfileBannerDismissed { get; set; }

    /// <summary>
    /// Written by Borea before 0.2.0, when a closed update banner stayed closed.
    /// It is a known key so that it does not stay with the unknown keys, it is never acted on, and it is removed when the file is read.
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

    /// <summary>Null in an older file, which reads as on.</summary>
    public bool? StaySignedInToGitHub { get; set; }

    /// <summary>The loader release whose update banner was closed, as "id version".</summary>
    public string? DismissedLoaderUpdate { get; set; }

    /// <summary>The Borea version that last showed its release notes at start, such as "0.7.0" or "0.7.0-dev.3".</summary>
    public string? LastSeenBoreaVersion { get; set; }

    public List<CustomThemePreferenceDto?>? CustomThemes { get; set; }

    /// <summary>
    /// The keys that this build does not know, such as a setting that a newer Borea added.
    /// They are written back unchanged on every save, so an older Borea loads such a file and does not remove the key.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }
}

/// <summary>
/// A custom theme is saved from its mapped values only, so it cannot keep an unknown key.
/// An unknown key in a theme makes the file invalid, because a save would otherwise remove it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class CustomThemePreferenceDto
{
    public required string Name { get; set; }

    public required string MainColor { get; set; }

    public required string SecondaryColor { get; set; }

    public required string GlobalPanelsColor { get; set; }

    public required string TextColor { get; set; }
}
