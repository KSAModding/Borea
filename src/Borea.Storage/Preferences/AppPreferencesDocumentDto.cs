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
