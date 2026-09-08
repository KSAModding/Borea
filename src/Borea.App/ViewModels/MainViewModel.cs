using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;

namespace Borea.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
    [ObservableProperty]
    private string _mainColor = "#248cc0";
    [ObservableProperty]
    private string _secondaryColor = "#26556c";
    [ObservableProperty]
    private string _globalPanelsColor = "#2029d2";
    [ObservableProperty]
    private string _textColor = "#000000";



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
    private Dictionary<string, string[]> _themes = new Dictionary<string, string[]>();
    [ObservableProperty]
    private string _currentTheme = "DefaultBlue";
    [RelayCommand]
    public void GetThemes() // used to get the themes from the json files
    {
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
            }
        }
        string settingsJson = File.ReadAllText("client/Settings.json");
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(settingsJson);
        CurrentTheme = settings != null && settings.ContainsKey("theme") ? settings["theme"] : "Error";
        SetTheme(CurrentTheme); // set the theme to the current theme
    }
    public void SetTheme(string themeName) // used to set the theme
    {
        if (Themes.ContainsKey(themeName))
        {
            CurrentTheme = themeName;
            MainColor = Themes[themeName][1];
            SecondaryColor = Themes[themeName][2];
            GlobalPanelsColor = Themes[themeName][3];
            TextColor = Themes[themeName][4];
            var settings = new Dictionary<string, string>
            {
                { "theme", themeName }
            };
            string settingsJson = JsonSerializer.Serialize(settings);
            File.WriteAllText("client/Settings.json", settingsJson);
        }
    }
}
