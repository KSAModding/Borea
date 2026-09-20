using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Help tab, which says whom to ask when something goes wrong and offers the
/// facts they will ask for. What a player can change lives on the tab that owns it.
/// </summary>
public partial class MainViewModel
{
    public const string ForumsUrl = "https://forums.ahwoo.com/forums/kitten-space-agency/guides-and-help/";

    [RelayCommand]
    private void ShowHelpSettings()
    {
        AboutMessage = null;
        AboutError = null;
        SettingsTab = SettingsTab.Help;
    }
}
