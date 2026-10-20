using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.UserContent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The vehicles and saves Borea installed in an instance (RFC 0085).</summary>
public partial class MainViewModel
{
    /// <summary>The records of an instance. A record file Borea cannot read gives none, so the instance still opens.</summary>
    private async Task<IReadOnlyList<InstalledContentRecord>> InstalledContentOfAsync(Guid instanceId)
    {
        if (_services is null)
            return [];

        try
        {
            return await _services.InstalledContent.ListAsync(instanceId);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// What the game does to each recorded vehicle and save of the instance that needs the mod, when the mod is removed
    /// or disabled, or null when none needs it. It only warns, because only an incompatible release blocks.
    /// </summary>
    internal string? ContentNeededByText(Instance instance, string modId)
    {
        var content = Instances.FirstOrDefault(item => item.InstanceId == instance.InstanceId)?.Content ?? [];
        var needing = RecordedContentNeeds.Needing(content, instance, modId);
        return needing.Count == 0
            ? null
            : string.Join(" ", needing.Select(record => record.Type == ContentType.Save
                ? Localization.FormatContentNeededBySave(record.FolderName)
                : Localization.FormatContentNeededByVehicle(record.FolderName)));
    }

    /// <summary>
    /// Shows on each row whose folder Borea installed what the record says: the listing, the installed version and a
    /// newer release. The files are compared after that, because a large save takes a while to read.
    /// </summary>
    private async Task ShowRecordedContentAsync(GameSaveSection section, Guid instanceId)
    {
        if (_services is not { } services)
            return;

        var type = section.Kind == GameSaveKind.Vehicle ? ContentType.Vehicle : ContentType.Save;
        var records = await InstalledContentOfAsync(instanceId);
        var recorded = new List<GameSaveItem>();
        foreach (var item in section.Items)
        {
            if (records.FirstOrDefault(record => record.Type == type && string.Equals(record.FolderName, item.FolderName, StringComparison.OrdinalIgnoreCase)) is not { } record)
                continue;

            ModMetadata? listing = null;
            ModVersionMetadata? newer = null;
            try
            {
                listing = await services.UserContent.FindAsync(record.ListingId);
                newer = await services.UserContent.GetLatestReleaseAsync(record.ListingId, services.Settings.ReleaseChannel);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
            {
                // without the index the row still shows the record and its changes
            }

            if (SelectedInstance?.InstanceId != instanceId)
                return;

            item.ShowRecord(record, listing, newer is { } release && release.Version.CompareTo(record.Version) > 0 ? release : null);
            recorded.Add(item);
        }

        foreach (var item in recorded)
        {
            InstalledContentChanges changes;
            try
            {
                changes = await services.InstalledContent.CompareAsync(instanceId, item.Record!);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // a compare that ends after the player opened another instance belongs to rows that are gone
            if (SelectedInstance?.InstanceId != instanceId)
                return;

            item.Changes = changes.IsUnchanged ? null : changes;
        }
    }

    /// <summary>Opens the page of the listing a recorded row came from, with the instance in the breadcrumb.</summary>
    internal async Task OpenRecordedContentPageAsync(GameSaveItem item)
    {
        if (item.Listing is not { } listing)
            return;

        await EnsureDiscoverLoadedAsync();
        await OpenContentFromInstanceAsync(IndexListing(listing.ModId) ?? new DiscoverItem(this, listing));
    }

    internal Task UpdateRecordedContentAsync(GameSaveItem item)
        => item.NewerRelease is { } release ? PlanContentAsync(item, item.InstanceId, release) : Task.CompletedTask;

    /// <summary>
    /// Removes a recorded row through the code of the CLI, which keeps the folder in the backups. A folder the player
    /// changed since the confirmation asks once more with the files it found.
    /// </summary>
    private async Task RemoveRecordedGameSaveAsync(GameSaveItem item, InstalledContentRecord record)
    {
        var asksAgain = false;
        var removed = await RunGameSaveActionAsync([item.InstanceId], () => Localization.FormatToastDeleteFailed(item.Name), async services =>
        {
            var result = await services.UserContentInstaller.RemoveAsync(item.InstanceId, record, discardChanges: item.Changes is not null);
            if (result.Outcome == UserContentOutcome.Changed && result.Changes is { } changes)
            {
                item.Changes = changes;
                asksAgain = true;
                return null;
            }

            if (!result.IsDone)
                throw new InvalidOperationException(ContentOutcomeText(result, record.Metadata, item.InstanceId));

            return () => Localization.FormatGameSaveDeleted(item.Name);
        });

        if (asksAgain)
        {
            item.IsConfirmingDelete = true;
            return;
        }

        if (removed)
            await ReloadInstancesAsync();
    }
}

/// <summary>What a save or vehicle row shows when Borea installed its folder, and the update of it.</summary>
public sealed partial class GameSaveItem : IContentPlanRow
{
    /// <summary>The record of the folder, or null for a folder that is the player's own.</summary>
    internal InstalledContentRecord? Record { get; private set; }

    /// <summary>The listing the folder came from, or null when the index does not list it now.</summary>
    internal ModMetadata? Listing { get; private set; }

    /// <summary>A newer release in the saved channel than the recorded one, or null.</summary>
    internal ModVersionMetadata? NewerRelease { get; private set; }

    public bool HasRecord => Record is not null;

    public string? ListingName => Listing?.Name;

    public string? InstalledVersionText => Record is { } record ? _owner.Localization.FormatContentDependencyInstalled(record.Version.ToString()) : null;

    public string? NewerText => NewerRelease is { } newer ? _owner.Localization.FormatPackMemberNewer(newer.Version.ToString()) : null;

    public string? UpdateText => NewerRelease is { } newer ? _owner.Localization.FormatContentUpdateTo(newer.Version.ToString()) : null;

    public bool CanUpdate => NewerRelease is not null && !IsInstalling && !IsConfirmingInstall;

    /// <summary>The files the player changed since the install, or null when nothing changed or the compare did not end yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyPropertyChangedFor(nameof(ChangedFilesText))]
    [NotifyPropertyChangedFor(nameof(DeleteConfirmText))]
    private InstalledContentChanges? _changes;

    public bool IsChanged => Changes is not null;

    public string? ChangedFilesText => Changes is { } changes ? MainViewModel.ChangedFilesText(changes) : null;

    /// <summary>A recorded folder the player changed names the files before it goes into the backups.</summary>
    public string DeleteConfirmText => ChangedFilesText is { } files ? _owner.Localization.FormatContentRemoveChanged(Name, files) : _owner.Localization.GameSaveDeleteConfirm;

    public UserContentPlan? PendingContentPlan { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private InstallRun? _run;

    [ObservableProperty]
    private string? _installError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    [NotifyPropertyChangedFor(nameof(AddedModsText))]
    private InstallPlan? _pendingPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyPropertyChangedFor(nameof(AddedModsText))]
    private InstallChoices? _choices;

    public bool IsConfirmingInstall => PendingPlan is not null || Choices is not null;

    public string ConfirmInstallText => _owner.ConfirmInstallText(InstallWarning, PendingPlan, Record?.Version.ToString());

    public string? AddedModsText => _owner.AddedModsText(PendingPlan, Choices);

    internal void ShowRecord(InstalledContentRecord record, ModMetadata? listing, ModVersionMetadata? newer)
    {
        Record = record;
        Listing = listing;
        NewerRelease = newer;
        OnPropertyChanged(nameof(HasRecord));
        OnPropertyChanged(nameof(ListingName));
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(NewerText));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(CanUpdate));
    }

    private void RefreshRecordText()
    {
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(NewerText));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(DeleteConfirmText));
        OnPropertyChanged(nameof(ConfirmInstallText));
        OnPropertyChanged(nameof(AddedModsText));
    }

    [RelayCommand]
    private Task OpenPageAsync() => _owner.OpenRecordedContentPageAsync(this);

    [RelayCommand]
    private Task UpdateAsync()
    {
        CloseEdits();
        return _owner.UpdateRecordedContentAsync(this);
    }

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmInstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);
}
