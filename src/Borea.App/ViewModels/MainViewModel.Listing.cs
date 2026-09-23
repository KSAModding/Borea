using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Borea.Composition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The "List or change your mod" page, opened from the side panel of Discover. Its editor lives as long as the App,
/// so leaving the page and coming back keeps the draft.
/// </summary>
public partial class MainViewModel
{
    private ListingEditor? _listingEditor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverSection))]
    private bool _currentWindowListing;

    public ListingEditor ListingEditor => _listingEditor ??= new ListingEditor(this);

    internal BoreaServices? Services => _services;

    [RelayCommand]
    internal Task OpenListingAsync()
    {
        LeaveContentPage();
        LeavePackPage();
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        IsTasksOpen = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
        CurrentWindowListing = true;
        return ListingEditor.OpenAsync();
    }

    /// <summary>Opens a GitHub page, and returns why it could not, or null. The message leaves out the URL, which can carry the whole file.</summary>
    internal string? OpenListingPage(string url)
    {
        try
        {
            OpenWithSystem(url);
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return exception.Message;
        }
    }

    partial void OnCurrentWindowHomeChanged(bool value) => LeaveListingPage(value);

    partial void OnCurrentWindowDiscoverChanged(bool value) => LeaveListingPage(value);

    partial void OnCurrentWindowLibraryChanged(bool value) => LeaveListingPage(value);

    partial void OnCurrentWindowInstanceChanged(bool value) => LeaveListingPage(value);

    partial void OnCurrentWindowContentChanged(bool value) => LeaveListingPage(value);

    partial void OnCurrentWindowPackChanged(bool value) => LeaveListingPage(value);

    private void LeaveListingPage(bool otherPageOpened)
    {
        if (!otherPageOpened)
            return;

        CurrentWindowListing = false;
        _listingEditor?.Leave();
    }
}
