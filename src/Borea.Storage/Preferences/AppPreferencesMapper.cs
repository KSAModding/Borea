using System.Globalization;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using Borea.Core.Updates;

namespace Borea.Storage.Preferences;

internal static class AppPreferencesMapper
{
    private const string StableName = "stable";

    private const string TestingName = "testing";

    private const string DevName = "dev";

    private const string ActiveInstanceName = "active-instance";

    private const string WithoutModLoaderName = "without-mod-loader";

    private const string PopularitySortName = "popularity";

    private const string RecentlyUpdatedSortName = "recently-updated";

    private const string NameSortName = "name";

    public static AppPreferencesDocumentDto ToDto(AppPreferences preferences) => new()
    {
        FormatVersion = FileAppPreferencesRepository.CurrentFormatVersion,
        SelectedTheme = preferences.SelectedThemeName,
        RegionalCulture = preferences.RegionalCultureName,
        UiCulture = preferences.UiCultureName,
        CheckForUpdatesAtStart = preferences.CheckForUpdatesAtStart,
        UpdateChannel = preferences.UpdateChannel switch
        {
            BoreaUpdateChannel.Testing => TestingName,
            BoreaUpdateChannel.Dev => DevName,
            _ => StableName,
        },
        ForeignFolderDeletionConfirmed = preferences.ForeignFolderDeletionConfirmed,
        LoadImagesFromAuthorHosts = preferences.LoadImagesFromAuthorHosts,
        HomeLaunch = preferences.HomeLaunch == HomeLaunchOption.WithoutModLoader ? WithoutModLoaderName : ActiveInstanceName,
        DiscoverSortOrder = preferences.DiscoverSortOrder switch
        {
            DiscoverSortOrder.RecentlyUpdated => RecentlyUpdatedSortName,
            DiscoverSortOrder.Name => NameSortName,
            _ => PopularitySortName,
        },
        SharedProfileBannerDismissed = preferences.SharedProfileBannerDismissed,
        DismissedBoreaRelease = preferences.DismissedBoreaRelease?.ToString(),
        DismissedGameRevision = preferences.DismissedGameRevision,
        FirstStartedAt = preferences.FirstStartedAt?.ToString("O", CultureInfo.InvariantCulture),
        FetchAnnouncements = preferences.FetchAnnouncements,
        DismissedAnnouncements = preferences.DismissedAnnouncements.Select(id => (string?)id).ToList(),
        OpenBoreaLinks = preferences.OpenBoreaLinks,
        CustomThemes = preferences.CustomThemes.Select(theme => (CustomThemePreferenceDto?)ToDto(theme)).ToList(),
    };

    public static AppPreferences FromDto(AppPreferencesDocumentDto dto)
        => new(dto.SelectedTheme, dto.CustomThemes?.Select(FromDto), NormalizeRegionalCulture(dto.RegionalCulture), NormalizeUiCulture(dto.UiCulture), dto.CheckForUpdatesAtStart ?? true, ReadUpdateChannel(dto.UpdateChannel), dto.ForeignFolderDeletionConfirmed ?? false, dto.LoadImagesFromAuthorHosts ?? true, ReadHomeLaunch(dto.HomeLaunch), ReadDiscoverSortOrder(dto.DiscoverSortOrder), dto.SharedProfileBannerDismissed ?? false, ModVersion.TryParse(dto.DismissedBoreaRelease, out var dismissed) ? dismissed : null, dto.DismissedGameRevision is >= 0 ? dto.DismissedGameRevision : null, ReadFirstStartedAt(dto.FirstStartedAt), dto.FetchAnnouncements ?? true, dto.DismissedAnnouncements?.OfType<string>(), dto.OpenBoreaLinks ?? true);

    private static DateTimeOffset? ReadFirstStartedAt(string? text)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    private static BoreaUpdateChannel ReadUpdateChannel(string? name)
        => name?.ToLowerInvariant() switch
        {
            TestingName => BoreaUpdateChannel.Testing,
            DevName => BoreaUpdateChannel.Dev,
            _ => BoreaUpdateChannel.Stable,
        };

    private static DiscoverSortOrder ReadDiscoverSortOrder(string? name)
        => name?.ToLowerInvariant() switch
        {
            RecentlyUpdatedSortName => DiscoverSortOrder.RecentlyUpdated,
            NameSortName => DiscoverSortOrder.Name,
            _ => DiscoverSortOrder.Popularity,
        };

    /// <summary>
    /// A UI language can be a custom culture such as "en-QP", so a name is kept
    /// when .NET creates a culture of that exact name.
    /// </summary>
    private static string? NormalizeUiCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return null;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase) ? culture.Name : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

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

    private static HomeLaunchOption ReadHomeLaunch(string? name)
        => string.Equals(name, WithoutModLoaderName, StringComparison.OrdinalIgnoreCase) ? HomeLaunchOption.WithoutModLoader : HomeLaunchOption.ActiveInstance;

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
