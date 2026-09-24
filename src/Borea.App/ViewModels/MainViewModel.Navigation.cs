using System;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The forward step for the forward button of a mouse (#492). It opens the page the
/// last back step left, as long as no other page was shown since.
/// </summary>
public partial class MainViewModel
{
    private Func<Task>? _forwardPage;

    /// <summary>Whether the page shows the back arrow.</summary>
    internal bool CanGoBack => CurrentWindowContent || CurrentWindowPack || CurrentWindowInstance || CurrentWindowListing;

    [RelayCommand]
    private Task GoForwardAsync()
    {
        var reopen = _forwardPage;
        _forwardPage = null;
        return reopen?.Invoke() ?? Task.CompletedTask;
    }

    private Func<Task>? PageToReopen()
    {
        // The origin of a content page stays set until another page opens, which also forgets the way forward.
        // A row is looked up again, because an index check on the page behind builds new rows.
        if (CurrentWindowContent && SelectedContent is { } content)
            return () => ShowContentAsync(_listings.FirstOrDefault(item => ModIds.Equals(item.ModId, content.ModId) && item.Source == content.Source) ?? content);

        if (CurrentWindowPack && SelectedPack is { } pack)
            return () => OpenPackAsync(_packs.FirstOrDefault(item => ModIds.Equals(item.PackId, pack.PackId) && item.Metadata.Source == pack.Metadata.Source) ?? pack);

        if (CurrentWindowInstance && SelectedInstance is { } opened)
            return () => Instances.FirstOrDefault(item => item.InstanceId == opened.InstanceId) is { } current ? OpenInstanceAsync(current) : Task.CompletedTask;

        return CurrentWindowListing ? OpenListingAsync : null;
    }

    partial void OnCurrentWindowHomeChanged(bool value) => OnPageShown(value);

    partial void OnCurrentWindowDiscoverChanged(bool value) => OnPageShown(value);

    partial void OnCurrentWindowLibraryChanged(bool value) => OnPageShown(value);

    partial void OnCurrentWindowInstanceChanged(bool value) => OnPageShown(value);

    partial void OnCurrentWindowContentChanged(bool value) => OnPageShown(value);

    partial void OnCurrentWindowPackChanged(bool value) => OnPageShown(value);

    partial void OnCurrentWindowListingChanged(bool value)
    {
        if (value)
            _forwardPage = null;
    }

    // a page that opens turns the other pages off itself, but not the listing page
    private void OnPageShown(bool shown)
    {
        if (!shown)
            return;

        _forwardPage = null;
        LeaveListingPage();
    }
}
