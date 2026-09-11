namespace Borea.Storage.Preferences;

internal sealed class AppPreferencesDocumentDto
{
    public int FormatVersion { get; set; }

    public string? SelectedTheme { get; set; }

    public string? RegionalCulture { get; set; }

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
