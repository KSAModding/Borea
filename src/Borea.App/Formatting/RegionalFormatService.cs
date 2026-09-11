using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Borea.App.Localization;

namespace Borea.App.Formatting;

public sealed class RegionalFormatService : INotifyPropertyChanged
{
    private readonly LocalizationService _localization;
    private readonly CultureInfo _systemCulture;

    private IReadOnlyList<RegionalFormatOption> _supportedFormats;
    private RegionalFormatOption _selectedFormat;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RegionalFormatOption> SupportedFormats => _supportedFormats;

    public RegionalFormatOption SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (value is null)
                return;

            SetFormat(ResolveFormat(value.CultureName));
        }
    }

    public string? SelectedCultureName => SelectedFormat.CultureName;

    public RegionalFormatService(LocalizationService localization)
        : this(localization, CultureInfo.CurrentCulture, selectedCultureName: null)
    {
    }

    public RegionalFormatService(
        LocalizationService localization,
        CultureInfo systemCulture,
        string? selectedCultureName)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _systemCulture = systemCulture ?? throw new ArgumentNullException(nameof(systemCulture));
        _supportedFormats = BuildSupportedFormats();
        _selectedFormat = _supportedFormats[0];
        TrySetCulture(selectedCultureName);
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public bool TrySetCulture(string? cultureName)
    {
        if (cultureName is null)
        {
            SetFormat(_supportedFormats[0]);
            return true;
        }

        if (string.IsNullOrWhiteSpace(cultureName))
        {
            SetFormat(_supportedFormats[0]);
            return false;
        }

        var format = _supportedFormats.FirstOrDefault(candidate =>
            string.Equals(candidate.CultureName, cultureName, StringComparison.OrdinalIgnoreCase));
        SetFormat(format ?? _supportedFormats[0]);
        return format is not null;
    }

    private IReadOnlyList<RegionalFormatOption> BuildSupportedFormats()
    {
        var systemDefault = new RegionalFormatOption(
            cultureName: null,
            $"{_localization.SystemDefaultRegionalFormat} ({_systemCulture.NativeName})",
            _systemCulture);

        var specificFormats = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(culture => !string.IsNullOrEmpty(culture.Name))
            .GroupBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(culture => new RegionalFormatOption(
                culture.Name,
                $"{culture.NativeName} ({culture.Name})",
                culture))
            .OrderBy(format => format.DisplayName, StringComparer.OrdinalIgnoreCase);

        return [systemDefault, .. specificFormats];
    }

    private RegionalFormatOption ResolveFormat(string? cultureName)
        => cultureName is null
            ? _supportedFormats[0]
            : _supportedFormats.FirstOrDefault(candidate =>
                string.Equals(candidate.CultureName, cultureName, StringComparison.OrdinalIgnoreCase))
                ?? _supportedFormats[0];

    private void SetFormat(RegionalFormatOption format)
    {
        CultureInfo.CurrentCulture = format.Culture;

        if (string.Equals(_selectedFormat.CultureName, format.CultureName, StringComparison.Ordinal))
            return;

        _selectedFormat = format;
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(SelectedCultureName));
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        var selectedCultureName = SelectedCultureName;
        _supportedFormats = BuildSupportedFormats();
        _selectedFormat = ResolveFormat(selectedCultureName);
        OnPropertyChanged(nameof(SupportedFormats));
        OnPropertyChanged(nameof(SelectedFormat));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
