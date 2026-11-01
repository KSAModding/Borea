using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>What the owner of a listing on a share host can do from the content page, and the dialog that does it.</summary>
public partial class MainViewModel
{
    /// <summary>The open change or take-down of a listing on a share host, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShareManageOpen))]
    private ShareManageDialog? _shareManage;

    public bool IsShareManageOpen => ShareManage is not null;

    /// <summary>What the player sent to a host in this session, per listing. The index shows a change only hours later, so the dialog starts from what the host holds, and the player can send the old text back.</summary>
    private readonly Dictionary<string, ShareListingChange> _sentShareChanges = new(ModIds.Comparer);

    /// <summary>The record of a share that says the player owns the open listing, or null.</summary>
    private SharedContentEntry? _contentOwnEntry;

    /// <summary>The record of a share comes from a publish that the host took, and the host still decides with not_owner.</summary>
    public bool CanManageContent => _contentOwnEntry is not null && _contentShareHost is not null;

    /// <summary>The page of the listing from the record, else the homepage of the listing while it is on the host, which writes that link. Borea never builds this address.</summary>
    private Uri? ContentManagePage => _contentOwnEntry is null || _contentShareHost is not { } host ? null
        : _contentOwnEntry.Page ?? (SelectedContent?.Links.TryGetValue("homepage", out var homepage) == true ? host.PageOnHost(homepage) : null);

    public string? ContentManageOnHostText => ContentManagePage is not null ? Localization.FormatShareManageOnHost(_contentShareHost!.Name) : null;

    internal void BeginShareManage(ShareManageTarget target, ShareManageAction action)
    {
        if (ShareManage is null)
            ShareManage = new ShareManageDialog(this, _sentShareChanges.TryGetValue(SentChangeKey(target), out var sent) ? target with { Fields = target.Fields.With(sent) } : target, action);
    }

    internal void RememberSentShareChange(ShareManageTarget target, ShareListingChange change)
    {
        var key = SentChangeKey(target);
        _sentShareChanges[key] = _sentShareChanges.TryGetValue(key, out var earlier)
            ? new ShareListingChange(change.Name ?? earlier.Name, change.Abstract ?? earlier.Abstract, change.Description ?? earlier.Description, change.License ?? earlier.License, change.Tags ?? earlier.Tags)
            : change;
    }

    private static string SentChangeKey(ShareManageTarget target) => $"{target.Type}/{target.Host.Key}/{target.ListingId}";

    internal void CloseShareManage(ShareManageDialog dialog)
    {
        if (ReferenceEquals(ShareManage, dialog))
            ShareManage = null;
    }

    private void ForgetContentOwner()
    {
        _contentOwnEntry = null;
        RefreshContentManage();
    }

    private void RefreshContentManage()
    {
        OnPropertyChanged(nameof(CanManageContent));
        OnPropertyChanged(nameof(ContentManageOnHostText));
    }

    private async Task ShowContentOwnerAsync(DiscoverItem item, ShareHost host)
    {
        var reference = item.Listing.Releases?.Hosts.FirstOrDefault(listed => listed.Kind == ReleaseHostKind.Share && listed.Host == host.Key)?.Reference;
        var entries = await _services!.SharedContent.ListAsync();
        if (!ReferenceEquals(SelectedContent, item))
            return;

        _contentOwnEntry = entries.LastOrDefault(entry => entry.Type == item.Type && entry.HostKey == host.Key && ModIds.Equals(entry.ListingId, reference ?? item.ModId));
        RefreshContentManage();
    }

    [RelayCommand]
    private void ChangeContentOnHost()
    {
        if (ContentManageTarget([]) is { } target)
            BeginShareManage(target, ShareManageAction.Change);
    }

    /// <summary>A failure to read the versions leaves the one of the record, and the link on the dialog still reaches all of them.</summary>
    [RelayCommand]
    private async Task TakeDownContentVersionAsync()
    {
        if (_services is not { } services || SelectedContent is not { } item || _contentOwnEntry is not { } entry)
            return;

        var versions = new List<string>();
        try
        {
            versions.AddRange((await services.UserContent.GetReleaseHistoryAsync(item.ModId)).Select(release => release.Version.ToString()));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
        }

        // The index holds a new version only hours later, so a version that only the record names is the newest.
        if (entry.Version.Length > 0 && !versions.Contains(entry.Version, StringComparer.Ordinal))
            versions.Insert(0, entry.Version);

        if (ReferenceEquals(SelectedContent, item) && ContentManageTarget(versions) is { } target)
            BeginShareManage(target, ShareManageAction.TakeDown);
    }

    [RelayCommand]
    private void OpenContentManagePage()
    {
        if (ContentManagePage is { } page)
            OpenLink(new ContentLink(page.AbsoluteUri, page.AbsoluteUri));
    }

    private ShareManageTarget? ContentManageTarget(IReadOnlyList<string> versions) =>
        SelectedContent is { } item && _contentShareHost is { } host && _contentOwnEntry is { } entry
            ? new ShareManageTarget(host, item.Type, entry.ListingId, item.Name, ContentManagePage, new ShareManageFields(item.Name, item.Abstract, item.Description, item.License, item.Listing.Tags), versions)
            : null;

    /// <summary>
    /// The record names the newest of the remaining versions, or no version when the dialog knows of none, as on the done step, so a next share does not say that the version is online.
    /// The index still lists the version until the host has told it.
    /// </summary>
    internal async Task ShowShareVersionTakenDownAsync(ShareManageTarget target, string version, IReadOnlyList<string> remaining)
    {
        if (_services is not { } services)
            return;

        var left = remaining.FirstOrDefault() ?? string.Empty;
        foreach (var entry in (await services.SharedContent.ListAsync()).Where(entry => entry.Type == target.Type && entry.HostKey == target.Host.Key && ModIds.Equals(entry.ListingId, target.ListingId) && entry.Version == version).ToList())
            await services.SharedContent.RecordAsync(entry with { Version = left }, author: null);

        if (_contentOwnEntry is { } own && own.Version == version && ModIds.Equals(own.ListingId, target.ListingId))
            _contentOwnEntry = own with { Version = left };

        _listingEditor?.ShowShareTakenDown(target.ListingId, version);
    }
}
