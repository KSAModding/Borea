using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Core.Preferences;

public sealed class AppPreferences
{
    public static AppPreferences Empty { get; } = new(selectedThemeName: null);

    public string? SelectedThemeName { get; }

    public string? RegionalCultureName { get; }

    /// <summary>
    /// The language of the user interface, as a culture name such as "de".
    /// Null means follow the system.
    /// </summary>
    public string? UiCultureName { get; }

    public IReadOnlyList<CustomThemePreference> CustomThemes { get; }

    /// <summary>Whether the App checks for a newer Borea release at start. On by default.</summary>
    public bool CheckForUpdatesAtStart { get; }

    /// <summary>Which Borea releases the update check reports. Stable by default.</summary>
    public BoreaUpdateChannel UpdateChannel { get; }

    /// <summary>Whether the user accepted that Borea deletes a mod folder it did not install. Off by default.</summary>
    public bool ForeignFolderDeletionConfirmed { get; }

    /// <summary>Whether listing images load from the hosts their authors chose, which learn the user's IP address. On by default.</summary>
    public bool LoadImagesFromAuthorHosts { get; }

    /// <summary>What the Home launch button starts, which is the option last chosen in its menu. The active instance by default.</summary>
    public HomeLaunchOption HomeLaunch { get; }

    /// <summary>How Discover orders its listings. Popularity by default.</summary>
    public DiscoverSortOrder DiscoverSortOrder { get; }

    /// <summary>Whether the user dismissed the offer to create an instance from the mods of the game profile. Off by default.</summary>
    public bool SharedProfileBannerDismissed { get; }

    /// <summary>The newest Borea release whose Home banner the user closed, or null. A newer release shows the banner again.</summary>
    public ModVersion? DismissedBoreaRelease { get; }

    /// <summary>The revision of the newest game build whose Home banner the user closed, or null. A higher revision shows the banner again.</summary>
    public int? DismissedGameRevision { get; }

    public AppPreferences(
        string? selectedThemeName,
        IEnumerable<CustomThemePreference>? customThemes = null,
        string? regionalCultureName = null,
        string? uiCultureName = null,
        bool checkForUpdatesAtStart = true,
        BoreaUpdateChannel updateChannel = BoreaUpdateChannel.Stable,
        bool foreignFolderDeletionConfirmed = false,
        bool loadImagesFromAuthorHosts = true,
        HomeLaunchOption homeLaunch = HomeLaunchOption.ActiveInstance,
        DiscoverSortOrder discoverSortOrder = DiscoverSortOrder.Popularity,
        bool sharedProfileBannerDismissed = false,
        ModVersion? dismissedBoreaRelease = null,
        int? dismissedGameRevision = null)
    {
        if (selectedThemeName is not null && string.IsNullOrWhiteSpace(selectedThemeName))
            throw new ArgumentException("Selected theme name, if provided, cannot be whitespace.", nameof(selectedThemeName));

        if (regionalCultureName is not null && string.IsNullOrWhiteSpace(regionalCultureName))
            throw new ArgumentException("Regional culture name, if provided, cannot be whitespace.", nameof(regionalCultureName));

        if (uiCultureName is not null && string.IsNullOrWhiteSpace(uiCultureName))
            throw new ArgumentException("UI culture name, if provided, cannot be whitespace.", nameof(uiCultureName));

        if (!Enum.IsDefined(updateChannel))
            throw new ArgumentException("The update channel is not defined.", nameof(updateChannel));

        if (!Enum.IsDefined(homeLaunch))
            throw new ArgumentException("The Home launch option is not defined.", nameof(homeLaunch));

        if (!Enum.IsDefined(discoverSortOrder))
            throw new ArgumentException("The Discover sort order is not defined.", nameof(discoverSortOrder));

        SelectedThemeName = selectedThemeName;
        RegionalCultureName = regionalCultureName;
        UiCultureName = uiCultureName;
        CheckForUpdatesAtStart = checkForUpdatesAtStart;
        UpdateChannel = updateChannel;
        ForeignFolderDeletionConfirmed = foreignFolderDeletionConfirmed;
        LoadImagesFromAuthorHosts = loadImagesFromAuthorHosts;
        HomeLaunch = homeLaunch;
        DiscoverSortOrder = discoverSortOrder;
        SharedProfileBannerDismissed = sharedProfileBannerDismissed;
        DismissedBoreaRelease = dismissedBoreaRelease;
        DismissedGameRevision = dismissedGameRevision;
        CustomThemes = BuildCustomThemes(customThemes, nameof(customThemes));
    }

    public AppPreferences WithRegionalCultureName(string? regionalCultureName)
        => new(SelectedThemeName, CustomThemes, regionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithUiCultureName(string? uiCultureName)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, uiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithSelectedThemeName(string? selectedThemeName)
        => new(selectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithCheckForUpdatesAtStart(bool checkForUpdatesAtStart)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, checkForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithUpdateChannel(BoreaUpdateChannel updateChannel)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, updateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithForeignFolderDeletionConfirmed(bool foreignFolderDeletionConfirmed)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, foreignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithLoadImagesFromAuthorHosts(bool loadImagesFromAuthorHosts)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, loadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithHomeLaunch(HomeLaunchOption homeLaunch)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, homeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithDiscoverSortOrder(DiscoverSortOrder discoverSortOrder)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, discoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithSharedProfileBannerDismissed(bool sharedProfileBannerDismissed)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, sharedProfileBannerDismissed, DismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithDismissedBoreaRelease(ModVersion? dismissedBoreaRelease)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, dismissedBoreaRelease, DismissedGameRevision);

    public AppPreferences WithDismissedGameRevision(int? dismissedGameRevision)
        => new(SelectedThemeName, CustomThemes, RegionalCultureName, UiCultureName, CheckForUpdatesAtStart, UpdateChannel, ForeignFolderDeletionConfirmed, LoadImagesFromAuthorHosts, HomeLaunch, DiscoverSortOrder, SharedProfileBannerDismissed, DismissedBoreaRelease, dismissedGameRevision);

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
