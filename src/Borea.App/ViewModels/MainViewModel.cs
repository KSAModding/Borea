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
    private string _currentWindowDiscover = "True";
    [ObservableProperty]
    private string _currentWindowLibrary = "False";
    [ObservableProperty]
    private string _currentWindowSettings = "False";
    [RelayCommand]
    public void SetMainWindowDiscover() // Will be used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowDiscover = "True";
        CurrentWindowLibrary = "False";
        CurrentWindowSettings = "False";
    }
    [RelayCommand]
    public void SetMainWindowLibrary() // Will be used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowDiscover = "False";
        CurrentWindowLibrary = "True";
        CurrentWindowSettings = "False";
    }
    [RelayCommand]
    public void SetMainWindowSettings() // Will be used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowDiscover = "False";
        CurrentWindowLibrary = "False";
        CurrentWindowSettings = "True";
    }
}
