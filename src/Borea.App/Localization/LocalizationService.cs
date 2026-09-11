using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Borea.App.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly SupportedCulture English = new("en", "English");
    private static readonly SupportedCulture German = new("de", "Deutsch");
    private static readonly IReadOnlyList<SupportedCulture> Cultures = [English, German];

    private SupportedCulture _selectedCulture = English;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SupportedCulture> SupportedCultures => Cultures;

    public SupportedCulture SelectedCulture
    {
        get => _selectedCulture;
        set
        {
            if (value is null)
                return;

            TrySetCulture(value.Name);
        }
    }

    public string SelectedCultureName => SelectedCulture.Name;

    public string BrandLogoPlaceholder => Resources.BrandLogoPlaceholder;

    public string NavigationHome => Resources.NavigationHome;

    public string NavigationLibrary => Resources.NavigationLibrary;

    public string NavigationDiscover => Resources.NavigationDiscover;

    public string NavigationSettings => Resources.NavigationSettings;

    public string PageHomeHeading => Resources.PageHomeHeading;

    public string PageDiscoverHeading => Resources.PageDiscoverHeading;

    public string PageLibraryHeading => Resources.PageLibraryHeading;

    public string SettingsLanguageLabel => Resources.SettingsLanguageLabel;

    public string SettingsRegionalFormatLabel => Resources.SettingsRegionalFormatLabel;

    public string SettingsThemeLabel => Resources.SettingsThemeLabel;

    public string SystemDefaultRegionalFormat => Resources.SystemDefaultRegionalFormat;

    public LocalizationService()
        : this(CultureInfo.CurrentUICulture)
    {
    }

    public LocalizationService(CultureInfo requestedCulture)
    {
        ArgumentNullException.ThrowIfNull(requestedCulture);
        SetCulture(ResolveSupportedCulture(requestedCulture));
    }

    public bool TrySetCulture(string? cultureName)
    {
        SupportedCulture? supportedCulture = null;

        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                supportedCulture = ResolveSupportedCulture(CultureInfo.GetCultureInfo(cultureName));
            }
            catch (CultureNotFoundException)
            {
            }
        }

        SetCulture(supportedCulture);
        return supportedCulture is not null;
    }

    public string FormatViewNotFound(string viewName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        return string.Format(CultureInfo.CurrentCulture, Resources.ViewNotFoundFormat, viewName);
    }

    public string FormatPreferenceSaveError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return string.Format(CultureInfo.CurrentCulture, Resources.PreferenceSaveErrorFormat, error);
    }

    private static SupportedCulture? ResolveSupportedCulture(CultureInfo culture)
    {
        for (var candidate = culture; candidate != CultureInfo.InvariantCulture; candidate = candidate.Parent)
        {
            var match = Cultures.FirstOrDefault(item =>
                string.Equals(item.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return null;
    }

    private void SetCulture(SupportedCulture? culture)
    {
        culture ??= English;
        Resources.Culture = culture.Culture;
        CultureInfo.CurrentUICulture = culture.Culture;

        if (ReferenceEquals(_selectedCulture, culture))
            return;

        _selectedCulture = culture;
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
