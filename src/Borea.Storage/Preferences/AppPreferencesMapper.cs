using System.Globalization;
using Borea.Core.Preferences;

namespace Borea.Storage.Preferences;

internal static class AppPreferencesMapper
{
    public static AppPreferencesDocumentDto ToDto(AppPreferences preferences) => new()
    {
        FormatVersion = FileAppPreferencesRepository.CurrentFormatVersion,
        SelectedTheme = preferences.SelectedThemeName,
        RegionalCulture = preferences.RegionalCultureName,
        CustomThemes = preferences.CustomThemes.Select(theme => (CustomThemePreferenceDto?)ToDto(theme)).ToList(),
    };

    public static AppPreferences FromDto(AppPreferencesDocumentDto dto)
        => new(dto.SelectedTheme, dto.CustomThemes?.Select(FromDto), NormalizeRegionalCulture(dto.RegionalCulture));

    private static string? NormalizeRegionalCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return null;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return culture.IsNeutralCulture ? null : culture.Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static CustomThemePreferenceDto ToDto(CustomThemePreference theme) => new()
    {
        Name = theme.Name,
        MainColor = theme.MainColor,
        SecondaryColor = theme.SecondaryColor,
        GlobalPanelsColor = theme.GlobalPanelsColor,
        TextColor = theme.TextColor,
    };

    private static CustomThemePreference FromDto(CustomThemePreferenceDto? theme)
    {
        if (theme is null)
            throw new ArgumentException("Custom themes cannot contain null.", nameof(theme));

        return new(theme.Name, theme.MainColor, theme.SecondaryColor, theme.GlobalPanelsColor, theme.TextColor);
    }
}
