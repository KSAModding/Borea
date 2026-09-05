using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    }
}
