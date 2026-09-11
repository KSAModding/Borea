namespace Borea.Core.Preferences;

public sealed class AppPreferences
{
    public static AppPreferences Empty { get; } = new(selectedThemeName: null);

    public string? SelectedThemeName { get; }

    public string? RegionalCultureName { get; }

    public IReadOnlyList<CustomThemePreference> CustomThemes { get; }

    public AppPreferences(
        string? selectedThemeName,
        IEnumerable<CustomThemePreference>? customThemes = null,
        string? regionalCultureName = null)
    {
        if (selectedThemeName is not null && string.IsNullOrWhiteSpace(selectedThemeName))
            throw new ArgumentException("Selected theme name, if provided, cannot be whitespace.", nameof(selectedThemeName));

        if (regionalCultureName is not null && string.IsNullOrWhiteSpace(regionalCultureName))
            throw new ArgumentException("Regional culture name, if provided, cannot be whitespace.", nameof(regionalCultureName));

        SelectedThemeName = selectedThemeName;
        RegionalCultureName = regionalCultureName;
        CustomThemes = BuildCustomThemes(customThemes, nameof(customThemes));
    }

    public AppPreferences WithRegionalCultureName(string? regionalCultureName)
        => new(SelectedThemeName, CustomThemes, regionalCultureName);

    public string ResolveSelectedThemeName(IReadOnlyCollection<string> bundledThemeNames, string defaultThemeName)
    {
        var bundledNames = BuildBundledThemeNames(bundledThemeNames);

        if (!bundledNames.Contains(defaultThemeName))
            throw new ArgumentException("The default theme must be one of the bundled themes.", nameof(defaultThemeName));

        if (SelectedThemeName is null)
            return defaultThemeName;

        var bundledMatch = bundledNames.FirstOrDefault(name => string.Equals(name, SelectedThemeName, StringComparison.Ordinal));
        if (bundledMatch is not null)
            return bundledMatch;

        var customMatch = CustomThemes.FirstOrDefault(theme => string.Equals(theme.Name, SelectedThemeName, StringComparison.Ordinal));
        return customMatch?.Name ?? defaultThemeName;
    }

    public void ValidateBundledThemeNames(IReadOnlyCollection<string> bundledThemeNames)
    {
        var bundledNames = BuildBundledThemeNames(bundledThemeNames);

        foreach (var customTheme in CustomThemes)
        {
            if (bundledNames.Contains(customTheme.Name))
                throw new ArgumentException($"Custom theme '{customTheme.Name}' conflicts with a bundled theme.", nameof(bundledThemeNames));
        }
    }

    private static IReadOnlyList<CustomThemePreference> BuildCustomThemes(IEnumerable<CustomThemePreference>? customThemes, string paramName)
    {
        var themes = new List<CustomThemePreference>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var theme in customThemes ?? [])
        {
            if (theme is null)
                throw new ArgumentException("Custom themes cannot contain null.", paramName);

            if (!names.Add(theme.Name))
                throw new ArgumentException($"Custom theme '{theme.Name}' appears more than once.", paramName);

            themes.Add(theme);
        }

        return themes.AsReadOnly();
    }

    private static HashSet<string> BuildBundledThemeNames(IReadOnlyCollection<string> bundledThemeNames)
    {
        if (bundledThemeNames is null)
            throw new ArgumentNullException(nameof(bundledThemeNames));

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in bundledThemeNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Bundled theme names cannot be null or whitespace.", nameof(bundledThemeNames));

            if (!names.Add(name))
                throw new ArgumentException($"Bundled theme '{name}' appears more than once.", nameof(bundledThemeNames));
        }

        return names;
    }
}
