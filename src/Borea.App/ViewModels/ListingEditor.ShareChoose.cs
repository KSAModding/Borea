using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The first step of a share: the vehicles or saves of every instance in one list. Next starts the share with the same
/// code as a click on a row of an instance, and what stops it shows under the list.
/// </summary>
public sealed partial class ListingEditor
{
    private GameSaveKind _shareChooseKind;
    private int _shareChooseLoads;
    private bool _hasShareProfileItems;

    /// <summary>The last reading of the rows, which tests wait for.</summary>
    internal Task ShareChooseLoad { get; private set; } = Task.CompletedTask;

    /// <summary>The compare of the row that was selected last, which tests wait for.</summary>
    internal Task ShareRowCompare { get; private set; } = Task.CompletedTask;

    public ObservableCollection<ShareChooseRow> ShareChooseRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinueShareChoose), nameof(ShowsShareChooseFirst), nameof(IsComparingShareRow))]
    private ShareChooseRow? _selectedShareRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareChooseEmptyText), nameof(ShareProfileInstance), nameof(ShowsShareChooseFirst), nameof(ShowsShareCreateInstance))]
    private bool _isLoadingShareChoose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinueShareChoose))]
    private bool _isShareGameRunning;

    /// <summary>What stops the share of the selected row, or why an instance could not be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareChooseEmptyText))]
    private string? _shareChooseError;

    /// <summary>Whether an entry left the full form of a draft with no share for this step, so that Back can return to that draft.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsShareActionBar))]
    private bool _hasFormBehindShareChoose;

    public bool IsShareChooseVehicle => _shareChooseKind == GameSaveKind.Vehicle;

    public bool IsShareChooseSave => _shareChooseKind == GameSaveKind.Save;

    public string ShareChooseHeadingText => _shareTarget is not { Listing: var target } ? Localization.ShareChooseHeading
        : IsShareChooseSave ? Localization.FormatShareNewVersionChooseSave(target.Name)
        : Localization.FormatShareNewVersionChooseVehicle(target.Name);

    public bool CanContinueShareChoose => !IsShareGameRunning && SelectedShareRow is { BlocksNext: false };

    public bool ShowsShareChooseFirst => SelectedShareRow is null && ShareChooseRows.Count > 0 && !IsLoadingShareChoose;

    /// <summary>An empty Choose step with no draft behind it has no Back and no Next, so the bar under the page would show only its rule.</summary>
    public bool ShowsShareActionBar => IsShareStep && !(IsShareChoose && ShareChooseRows.Count == 0 && !HasFormBehindShareChoose);

    public bool IsComparingShareRow => SelectedShareRow is { IsUnchangedCopy: null, Item.Record: not null };

    /// <summary>Null while rows load or show, and when the error under the list already says why there are none.</summary>
    public string? ShareChooseEmptyText =>
        IsLoadingShareChoose || ShareChooseRows.Count > 0 || ShareChooseError is not null ? null
        : _owner.Instances.Count == 0 ? Localization.ShareNoInstance
        : _hasShareProfileItems ? IsShareChooseSave ? Localization.ShareProfileSaves : Localization.ShareProfileVehicles
        : IsShareChooseSave ? Localization.ShareNoSaves : Localization.ShareNoVehicles;

    public bool ShowsShareCreateInstance => !IsLoadingShareChoose && _owner.Instances.Count == 0;

    /// <summary>The instance whose page has the copy from the game profile, or null when the player has to pick one in the Library.</summary>
    public InstanceItem? ShareProfileInstance =>
        IsLoadingShareChoose || !_hasShareProfileItems ? null
        : _owner.ActiveInstance ?? (_owner.Instances.Count == 1 ? _owner.Instances[0] : null);

    /// <summary>Whether an entry with no item at hand would lose the work of a share that stands on Describe or Publish, or the pictures that it still sends or can try again.</summary>
    internal bool HasShareOnTheWay => _share is not null && (ShareFlowStep is ShareFlowStep.Describe or ShareFlowStep.Publish || IsSendingSharePictures || CanRetrySharePictures);

    /// <summary>Opens the step. A share on Publish loses its upload here, and a share on Describe keeps its draft until another item is picked.</summary>
    /// <param name="select">The row to select, which goes before the one candidate that is selected by itself.</param>
    /// <param name="problem">What stops the share of that row, for the line under the list.</param>
    internal Task OpenShareChooseAsync(GameSaveKind kind, (Guid InstanceId, string FolderName)? select = null, string? problem = null)
    {
        if (IsShareDone)
            ForgetShare();

        if (IsFormStep && _share is null)
            HasFormBehindShareChoose = true;

        ForgetShareHostRun();
        Step = ListingStep.Share;
        ShareFlowStep = ShareFlowStep.Choose;
        RefreshShareSteps();
        OnPropertyChanged(nameof(ShowsPreviewCard));
        return LoadShareChooseAsync(kind, select, problem);
    }

    /// <summary>Opens the step for a new version of a listing that the player chose, with the item that most likely belongs to it selected.</summary>
    /// <param name="select">The row that the player clicked, which goes before the item that the step would select.</param>
    internal Task OpenShareNewVersionAsync(GameSaveKind kind, ShareListing target, (Guid InstanceId, string FolderName)? select = null)
    {
        if (IsShareDone)
            ForgetShare();

        SetShareTarget((kind, target));
        return OpenShareChooseAsync(kind, select);
    }

    /// <summary>The link for mod authors leads to the start or to the draft behind the list, because a list with nothing picked holds no work. A share that has begun stays, and "Start over" is there for it.</summary>
    internal void LeaveShareStepWithNoShare()
    {
        if (!IsShareStep || _share is not null)
            return;

        if (HasFormBehindShareChoose)
            BackToShareForm();
        else
            Step = ListingStep.Start;
    }

    /// <summary>The player can come back to the step after playing or after making an instance, so the page reads the rows again.</summary>
    private Task ReloadShareChooseAsync() =>
        LoadShareChooseAsync(_shareChooseKind, SelectedShareRow is { Item: var item } ? (item.InstanceId, item.FolderName) : null, problem: null);

    /// <summary>A new instance can end an empty state. Rows that show stay as they are, so that an install that ends elsewhere does not clear the list under the player.</summary>
    internal Task ReloadEmptyShareChooseAsync() =>
        IsShareStep && IsShareChoose && ShareChooseRows.Count == 0 && !IsLoadingShareChoose ? ReloadShareChooseAsync() : Task.CompletedTask;

    private Task LoadShareChooseAsync(GameSaveKind kind, (Guid InstanceId, string FolderName)? select, string? problem)
    {
        // The listing takes only items of its own kind, so a list of the other kind is an entry with no listing in mind.
        if (_shareTarget is { } target && target.Kind != kind)
            SetShareTarget(null);

        _shareChooseKind = kind;
        OnPropertyChanged(nameof(IsShareChooseVehicle));
        OnPropertyChanged(nameof(IsShareChooseSave));
        OnPropertyChanged(nameof(ShareChooseHeadingText));
        return ShareChooseLoad = ReadShareChooseRowsAsync(++_shareChooseLoads, select, problem);
    }

    /// <summary>Reads one instance at a time, the active one first and then by last played. A later load drops this one.</summary>
    private async Task ReadShareChooseRowsAsync(int load, (Guid InstanceId, string FolderName)? select, string? problem)
    {
        IsLoadingShareChoose = true;
        SelectedShareRow = null;
        ShareChooseRows.Clear();
        ShareChooseError = null;
        _hasShareProfileItems = false;
        CheckShareGameRunning();

        foreach (var instance in _owner.Instances.OrderByDescending(item => item.IsActive).ThenByDescending(item => item.LastPlayedAt).ToList())
        {
            try
            {
                var items = await _owner.ListShareCandidatesAsync(instance, _shareChooseKind);
                if (load != _shareChooseLoads)
                    return;

                foreach (var item in items)
                    ShareChooseRows.Add(new ShareChooseRow(Localization, item, instance, item == items[0], ShareSourceName(item.Record), IsOwnCopyOfShareTarget(item)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problem ??= Localization.FormatShareErrorFile(instance.Name);
            }
        }

        var hasProfileItems = ShareChooseRows.Count == 0 && await HasShareProfileItemsAsync();
        var targetRow = await ShareTargetRowAsync();
        if (load != _shareChooseLoads)
            return;

        _hasShareProfileItems = hasProfileItems;
        IsLoadingShareChoose = false;

        // With a chosen listing, the one row is selected by itself only when it fits, because the heading says that the row belongs to the listing.
        SelectedShareRow = ShareChooseRows.FirstOrDefault(row => row.Item.InstanceId == select?.InstanceId && string.Equals(row.Item.FolderName, select?.FolderName, StringComparison.OrdinalIgnoreCase))
            ?? SelectedShareRow
            ?? targetRow
            ?? (_shareTarget is null && ShareChooseRows.Count == 1 ? ShareChooseRows[0] : null);
        ShareChooseError = problem;
    }

    private async Task<bool> HasShareProfileItemsAsync()
    {
        if (_owner.Services is not { } services)
            return false;

        try
        {
            return await services.GameSaves.HasSharedProfileItemsAsync(_shareChooseKind);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The name that the index holds for the source of a recorded row, or its id when the index no longer holds it. Null for a row with no record.</summary>
    private string? ShareSourceName(InstalledContentRecord? record)
    {
        if (record is null)
            return null;

        return ShareChooseListings()?.FirstOrDefault(listing => ModIds.Equals(listing.Id, record.ListingId))?.Authored?.Name ?? record.ListingId;
    }

    private IReadOnlyList<ContentIndexListing>? ShareChooseListings() => IsShareChooseSave ? _snapshot?.Saves : _snapshot?.Vehicles;

    /// <summary>
    /// Whether the row is an installed copy of the listing that the player chose to add a version to, and this computer shared that listing.
    /// Without a record of the share, the page of any listing on a share host leads here, so the copy keeps its warning about the author.
    /// </summary>
    private bool IsOwnCopyOfShareTarget(GameSaveItem item) =>
        item.Record is { } record && _shareTarget is { Listing: var target } && ModIds.Equals(record.ListingId, target.Id)
        && (item.SharedListingIds.Contains(target.Id) || item.SharedListingIds.Contains(target.Reference));

    /// <summary>
    /// The row of the item that a new version of the chosen listing most likely comes from: a folder that this computer shared to it,
    /// then an installed copy of it, and then a folder that its releases install into. Null when no listing is chosen or no row fits.
    /// </summary>
    private async Task<ShareChooseRow?> ShareTargetRowAsync()
    {
        if (_shareTarget is not { Listing: var target } || _owner.Services is not { } services || ShareChooseRows.Count == 0)
            return null;

        var type = _shareChooseKind.ToContentType();
        var shared = new List<string>();
        try
        {
            shared.AddRange((await services.SharedContent.ListAsync())
                .Where(entry => entry.Type == type && entry.HostKey == target.HostKey && (ModIds.Equals(entry.ListingId, target.Id) || ModIds.Equals(entry.ListingId, target.Reference)))
                .Select(entry => entry.FolderName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The other two rules can still select the row, and the player can always pick one.
        }

        var folders = ShareChooseListings()?.FirstOrDefault(listing => ModIds.Equals(listing.Id, target.Id))?.Releases.Select(release => release.Install?.Folder).OfType<string>().ToList() ?? [];
        return ShareChooseRows.FirstOrDefault(row => shared.Contains(row.Item.FolderName, StringComparer.OrdinalIgnoreCase))
            ?? ShareChooseRows.FirstOrDefault(row => ModIds.Equals(row.Item.Record?.ListingId, target.Id))
            ?? ShareChooseRows.FirstOrDefault(row => folders.Contains(row.Item.FolderName, StringComparer.OrdinalIgnoreCase));
    }

    partial void OnSelectedShareRowChanged(ShareChooseRow? oldValue, ShareChooseRow? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;

        ShareChooseError = null;
        CheckShareGameRunning();
        if (newValue is null)
            return;

        newValue.IsSelected = true;
        ShareRowCompare = CompareShareRowAsync(newValue);
    }

    /// <summary>Only the selected row is compared, because a compare reads every file of the item, and Next waits for it.</summary>
    private async Task CompareShareRowAsync(ShareChooseRow row)
    {
        if (row.Item.Record is not { } record || row.IsUnchangedCopy is not null || _owner.Services is not { } services)
            return;

        try
        {
            row.IsUnchangedCopy = (await services.InstalledContent.CompareAsync(row.Item.InstanceId, record)).IsUnchanged;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            row.IsUnchangedCopy = false;
        }

        OnPropertyChanged(nameof(CanContinueShareChoose));
        OnPropertyChanged(nameof(IsComparingShareRow));
    }

    /// <summary>The game can start or end at any time and gives no signal when it ends outside Borea, so the step asks when it opens, on a row, on Next and on the link of the banner.</summary>
    [RelayCommand]
    private void CheckShareGameRunning() => IsShareGameRunning = _owner.Services is { } services && _owner.IsGameRunning(services);

    [RelayCommand]
    private void ShowShareChooseVehicles() => _ = LoadShareChooseAsync(GameSaveKind.Vehicle, select: null, problem: null);

    [RelayCommand]
    private void ShowShareChooseSaves() => _ = LoadShareChooseAsync(GameSaveKind.Save, select: null, problem: null);

    [RelayCommand]
    private Task OpenShareProfileInstanceAsync() => ShareProfileInstance is { } instance ? _owner.OpenInstanceAsync(instance) : Task.CompletedTask;

    /// <summary>The same item keeps the draft of a new listing, unless the player chose a listing for it since. A new version starts again, so the question comes back and the player can change the answer. Another item starts a new share, or says why it cannot.</summary>
    [RelayCommand]
    private async Task ContinueShareChooseAsync()
    {
        CheckShareGameRunning();
        if (!CanContinueShareChoose || SelectedShareRow is not { Item: var item })
            return;

        if (!IsNewVersionShare && _shareTarget is null && _share is { } share && share.InstanceId == item.InstanceId && share.Entry.Kind == item.Entry.Kind && string.Equals(share.Entry.FolderName, item.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            ShareFlowStep = ShareFlowStep.Describe;
            return;
        }

        ShareChooseError = await _owner.TryStartShareAsync(item);
    }

    [RelayCommand]
    private void BackToShareChoose()
    {
        if (_share is { } share)
            _ = OpenShareChooseAsync(share.Entry.Kind, (share.InstanceId, share.Entry.FolderName));
    }

    [RelayCommand]
    private void BackToShareForm()
    {
        HasFormBehindShareChoose = false;
        Step = ListingStep.Form;
        Refresh();
    }

    [RelayCommand]
    private void ShareAnother()
    {
        ForgetShareTarget();
        _ = OpenShareChooseAsync(_share?.Entry.Kind ?? _shareChooseKind);
    }

    private void RefreshShareChooseText()
    {
        OnPropertyChanged(nameof(ShareChooseEmptyText));
        OnPropertyChanged(nameof(ShareChooseHeadingText));
        foreach (var row in ShareChooseRows)
            row.RefreshText();
    }
}

/// <summary>Wraps a <see cref="GameSaveItem"/>, whose own IsSelected drives the profile chooser.</summary>
public sealed partial class ShareChooseRow : ObservableObject
{
    private readonly LocalizationService _text;
    private readonly string? _sourceName;
    private readonly bool _isOfTargetListing;

    /// <param name="isOfTargetListing">Whether the row is an installed copy of the listing that the player chose to add a version to, and this computer shared that listing.</param>
    internal ShareChooseRow(LocalizationService text, GameSaveItem item, InstanceItem instance, bool isFirstOfInstance, string? sourceName, bool isOfTargetListing)
    {
        _text = text;
        _sourceName = sourceName;
        _isOfTargetListing = isOfTargetListing;
        Item = item;
        InstanceName = isFirstOfInstance ? instance.Name : null;
        IsFirstOfActiveInstance = isFirstOfInstance && instance.IsActive;
    }

    public GameSaveItem Item { get; }

    /// <summary>The instance above the first row of that instance, or null.</summary>
    public string? InstanceName { get; }

    public bool IsFirstOfActiveInstance { get; }

    public string? FromText => _sourceName is { } name ? _text.FormatSharePickFrom(name) : null;

    public bool NeedsNewName => !GameSaveName.IsValid(Item.Name);

    public string? NeedsNewNameTip => NeedsNewName ? _text.FormatGameSaveShareRename(Item.Name) : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteText))]
    private bool _isSelected;

    /// <summary>Null until the compare ends. A compare that failed counts as changed, so the note still names the source.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteText))]
    private bool? _isUnchangedCopy;

    internal bool BlocksNext => Item.Record is not null && IsUnchangedCopy != false;

    /// <summary>What the install record says about the selected row. A changed copy of the chosen listing gets no warning when this computer shared that listing, because the copy is then the player's own.</summary>
    public string? NoteText =>
        !IsSelected || _sourceName is not { } source || IsUnchangedCopy is not { } unchanged ? null
        : unchanged ? _text.FormatShareUnchangedCopy(source)
        : _isOfTargetListing ? null
        : Item.IsVehicle ? _text.FormatShareChangedVehicle(source)
        : _text.FormatShareChangedSave(source);

    internal void RefreshText()
    {
        Item.RefreshText();
        OnPropertyChanged(nameof(FromText));
        OnPropertyChanged(nameof(NeedsNewNameTip));
        OnPropertyChanged(nameof(NoteText));
    }
}
