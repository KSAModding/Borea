using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
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

    /// <summary>Opens the listing page with a new pack of the mods of an instance, for a pack author who tested that set.</summary>
    internal async Task MakePackFromInstanceAsync(Guid instanceId)
    {
        if (_services is not { } services)
            return;

        try
        {
            // Only a scan updates the stored manual folders, and the author can add or remove one by hand after the last scan.
            if (await services.Instances.GetByIdAsync(instanceId) is not null)
                await services.ForeignModAdopter.ScanAsync(instanceId);

            if (await services.Instances.GetByIdAsync(instanceId) is not { } instance)
            {
                await ReloadInstancesAsync();
                return;
            }

            var manifest = await services.ModState.GetEntriesAsync(instanceId);
            var content = await InstalledContentOfAsync(services, instanceId);
            var loaderStart = await LoaderStartOrNullAsync(services, instance);
            await OpenListingAsync();
            if (!ListingEditor.StartPackFromInstance(instance, manifest, loaderStart, content))
                ShowErrorToast(() => Localization.FormatToastMakePackFailed(instance.Name), Localization.ToastMakePackNoIndex);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var instanceName = InstanceName(instanceId);
            ShowErrorToast(() => Localization.FormatToastMakePackFailed(instanceName), exception.Message);
        }
    }

    /// <summary>Each vehicle and save folder of the instance, with its install record and whether the player changed it since.</summary>
    private static async Task<List<ListingInstalledContent>> InstalledContentOfAsync(BoreaServices services, Guid instanceId)
    {
        var records = await services.InstalledContent.ListAsync(instanceId);
        var content = new List<ListingInstalledContent>();
        foreach (var (kind, type) in new[] { (GameSaveKind.Vehicle, ContentType.Vehicle), (GameSaveKind.Save, ContentType.Save) })
        {
            foreach (var entry in await services.GameSaves.ListAsync(instanceId, kind))
            {
                var record = records.FirstOrDefault(item => item.Type == type && string.Equals(item.FolderName, entry.FolderName, StringComparison.OrdinalIgnoreCase));
                var changed = record is not null && !(await services.InstalledContent.CompareAsync(instanceId, record)).IsUnchanged;
                content.Add(new ListingInstalledContent(type, entry.FolderName, record, changed));
            }
        }

        return content;
    }

    /// <summary>
    /// The loader version that starts <paramref name="instance"/>, chosen as Play chooses it, or null when Borea cannot
    /// read the listings. The pack then names the loader without a version.
    /// </summary>
    private static async Task<InstalledLoaderVersion?> LoaderStartOrNullAsync(BoreaServices services, Instance instance)
    {
        try
        {
            return (await ResolveLaunchLoaderAsync(services, instance)).Start;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
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

    private void LeaveListingPage()
    {
        CurrentWindowListing = false;
        _listingEditor?.Leave();
    }
}
