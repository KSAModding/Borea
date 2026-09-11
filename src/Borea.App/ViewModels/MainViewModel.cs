using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.Core.Preferences;

namespace Borea.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    internal static IReadOnlyCollection<string> BundledThemeNames { get; } = ["Borealis", "Light", "Dark"];

    private readonly IAppPreferencesRepository? _appPreferencesRepository;
    private readonly SemaphoreSlim _preferenceSaveLock = new(1, 1);
    private AppPreferences _appPreferences;

    public LocalizationService Localization { get; }

    public RegionalFormatService RegionalFormat { get; }

    public RegionalFormatOption SelectedRegionalFormat
    {
        get => RegionalFormat.SelectedFormat;
        set
        {
            if (value is null)
                return;

            RegionalFormat.SelectedFormat = value;
            OnPropertyChanged();
            _ = SaveRegionalFormatAsync();
        }
    }

    [ObservableProperty]
    private string? _preferenceSaveError;

    [ObservableProperty]
    private string _mainColor = "#248cc0";
    [ObservableProperty]
    private string _secondaryColor = "#26556c";
    [ObservableProperty]
    private string _globalPanelsColor = "#2029d2";
    [ObservableProperty]
    private string _textColor = "#ffffff";



    //windows
    [ObservableProperty]
    private bool _currentWindowHome = true;
    [ObservableProperty]
    private bool _currentWindowDiscover = false;
    [ObservableProperty]
    private bool _currentWindowLibrary = false;
    [ObservableProperty]
    private bool _currentWindowSettings = false;
    [RelayCommand]
    public void SetMainWindowHome() // used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowHome = true;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowSettings = false;
    }
    [RelayCommand]
    public void SetMainWindowDiscover() // used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowHome = false;
        CurrentWindowDiscover = true;
        CurrentWindowLibrary = false;
        CurrentWindowSettings = false;
    }
    [RelayCommand]
    public void SetMainWindowLibrary() // used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = true;
        CurrentWindowSettings = false;
    }
    [RelayCommand]
    public void SetMainWindowSettings() // used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowSettings = true;
        GetThemes(); // get the themes when the settings window is opened
    }



    //themes
    [ObservableProperty]
    private string[] _themeNames = BundledThemeNames.ToArray();
    [ObservableProperty]
    private Dictionary<string, string[]> _themes = new Dictionary<string, string[]>();
    [ObservableProperty]
    private string _currentTheme = "Borealis";

    public MainViewModel()
        : this(new LocalizationService())
    {
    }

    public MainViewModel(LocalizationService localization)
        : this(
            localization,
            new RegionalFormatService(localization),
            appPreferencesRepository: null,
            AppPreferences.Empty)
    {
    }

    public MainViewModel(
        LocalizationService localization,
        RegionalFormatService regionalFormat,
        IAppPreferencesRepository? appPreferencesRepository,
        AppPreferences appPreferences)
    {
        Localization = localization ?? throw new ArgumentNullException(nameof(localization));
        RegionalFormat = regionalFormat ?? throw new ArgumentNullException(nameof(regionalFormat));
        _appPreferencesRepository = appPreferencesRepository;
        _appPreferences = appPreferences ?? throw new ArgumentNullException(nameof(appPreferences));
        RegionalFormat.PropertyChanged += OnRegionalFormatChanged;
    }

    private async Task SaveRegionalFormatAsync()
    {
        if (_appPreferencesRepository is null)
            return;

        await _preferenceSaveLock.WaitAsync();
        try
        {
            var regionalCultureName = RegionalFormat.SelectedCultureName;
            if (string.Equals(_appPreferences.RegionalCultureName, regionalCultureName, StringComparison.Ordinal))
            {
                PreferenceSaveError = null;
                return;
            }

            var updatedPreferences = _appPreferences.WithRegionalCultureName(regionalCultureName);
            await _appPreferencesRepository.SaveAsync(updatedPreferences, BundledThemeNames);
            _appPreferences = updatedPreferences;
            PreferenceSaveError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            PreferenceSaveError = Localization.FormatPreferenceSaveError(exception.Message);
        }
        finally
        {
            _preferenceSaveLock.Release();
        }
    }

    private void OnRegionalFormatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegionalFormatService.SelectedFormat))
            OnPropertyChanged(nameof(SelectedRegionalFormat));
    }

    [RelayCommand]
    public void GetThemes() // used to get the themes from the json files
    {
        Themes = new Dictionary<string, string[]>();
        ThemeNames = BundledThemeNames.ToArray();
        string themesJson = File.ReadAllText("client/BoreaDefaultThemes.json");
        var themes = JsonSerializer.Deserialize<Dictionary<string, string[]>>(themesJson);
        if (themes != null)
        {
            Themes = themes;
        }

        string themesJson2 = File.ReadAllText("client/CustomThemes.json");
        var themes2 = JsonSerializer.Deserialize<Dictionary<string, string[]>>(themesJson2);
        if (themes2 != null)
        {
            foreach (var kvp in themes2)
            {
                Themes.Add(kvp.Key, kvp.Value);
                ThemeNames = ThemeNames.Concat(new string[] { kvp.Key }).ToArray();
            }
        }
        string settingsJson = File.ReadAllText("client/Settings.json");
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(settingsJson);
        CurrentTheme = settings != null && settings.ContainsKey("theme") ? settings["theme"] : "Borealis";
        SetTheme(CurrentTheme); // set the theme to the current theme
    }
    public void SetTheme(string themeName) // used to set the theme
    {
        if (Themes.ContainsKey(themeName))
        {
            CurrentTheme = themeName;
            MainColor = Themes[themeName][0];
            SecondaryColor = Themes[themeName][1];
            GlobalPanelsColor = Themes[themeName][2];
            TextColor = Themes[themeName][3];
            var settings = new Dictionary<string, string>
            {
                { "theme", themeName }
            };
            string settingsJson = JsonSerializer.Serialize(settings);
            File.WriteAllText("client/Settings.json", settingsJson);
        }
    }
    partial void OnCurrentThemeChanged(string value)
    {
        SetTheme(value);
    }
}
