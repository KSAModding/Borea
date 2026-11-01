using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
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

    /// <summary>The mods that the manifest of an instance disables. A manifest Borea cannot read gives none.</summary>
    private async Task<IReadOnlyList<string>> InactiveModIdsAsync(Guid instanceId)
    {
        if (_services is null)
            return [];

        try
        {
            return (await _services.ModState.GetEntriesAsync(instanceId)).Where(entry => !entry.Enabled).Select(entry => entry.ModId).ToList();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The mods of an instance that the game does not load. The rows of the open instance hold the switches as the player set them.</summary>
    internal IReadOnlyList<string> InactiveModIds(InstanceItem instance)
    {
        var rows = _content.Where(row => row.InstanceId == instance.InstanceId).ToList();
        return rows.Count > 0 ? rows.Where(row => !row.IsEnabled).Select(row => row.ModId).ToList() : instance.InactiveModIds;
    }

    /// <summary>
    /// What the game does to each recorded vehicle and save of the instance that needs the mod, when the mod is removed
    /// or disabled, or null when none needs it. It only warns, because only an incompatible release blocks.
    /// </summary>
    internal string? ContentNeededByText(Instance instance, string modId)
    {
        var item = Instances.FirstOrDefault(item => item.InstanceId == instance.InstanceId);
        if (item is null || item.Content.Count == 0)
            return null;

        // the row of the mod itself may already show the switch off while it asks
        var inactive = InactiveModIds(item).Where(id => !ModIds.Equals(id, modId));
        var needing = RecordedContentNeeds.Needing(item.Content, instance, modId, inactive);
        return needing.Count == 0
            ? null
            : string.Join(" ", needing.Select(record => record.Type == ContentType.Save
                ? Localization.FormatContentNeededBySave(record.FolderName)
                : Localization.FormatContentNeededByVehicle(record.FolderName)));
    }

    /// <summary>
    /// Shows on each row whose folder Borea installed what the record says: the listing, the installed version and a
    /// newer release. The files are compared in a task that this does not wait for, because a large save takes a while to read.
    /// </summary>
    private async Task ShowRecordedContentAsync(GameSaveSection section, Instance instance)
    {
        if (_services is not { } services)
            return;

        var instanceId = instance.InstanceId;
        var type = section.Kind.ToContentType();
        var records = await InstalledContentOfAsync(instanceId);
        var recorded = new List<GameSaveItem>();
        foreach (var item in section.Items)
        {
            if (records.FirstOrDefault(record => record.Type == type && string.Equals(record.FolderName, item.FolderName, StringComparison.OrdinalIgnoreCase)) is not { } record)
                continue;

            ModMetadata? listing = null;
            try
            {
                listing = await services.UserContent.FindAsync(record.ListingId);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
            {
                // without the index the row still shows the record and its changes
            }

            var newer = await NewerRecordedReleaseAsync(services, instance, record);
            if (SelectedInstance?.InstanceId != instanceId)
                return;

            item.ShowRecord(record, listing, newer);
            recorded.Add(item);
        }

        OnPropertyChanged(nameof(HasUpdates));
        var previous = _recordedContentCompare;
        var compare = CompareRecordedContentAsync(services, recorded, instanceId);
        _recordedContentCompare = previous.IsCompleted ? compare : Task.WhenAll(previous, compare);
    }

    private Task _recordedContentCompare = Task.CompletedTask;

    internal Task WhenRecordedContentComparedAsync() => _recordedContentCompare;

    /// <summary>Started without being awaited, so that the page and the next section do not wait for every file to be read.</summary>
    private async Task CompareRecordedContentAsync(BoreaServices services, IReadOnlyList<GameSaveItem> recorded, Guid instanceId)
    {
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

    /// <summary>Holds the update slot of the instance as the update of a mod does, so the other rows and "Update all" wait for it.</summary>
    internal Task UpdateRecordedContentAsync(GameSaveItem item)
        => HoldUpdateSlotAsync(item, () => PlanRecordedUpdateAsync(item));

    internal Task ConfirmRecordedUpdateAsync(GameSaveItem item)
        => HoldUpdateSlotAsync(item, () => ConfirmInstallAsync(item));

    /// <summary>The update reloads the rows itself when it ran, so the slot only has to wait for it.</summary>
    private Task HoldUpdateSlotAsync(GameSaveItem item, Func<Task> update)
        => RunUpdateAsync(item, item.InstanceId, async () =>
        {
            await update();
            return false;
        });

    private Task PlanRecordedUpdateAsync(GameSaveItem item)
    {
        item.CloseEdits();
        return item.NewerRelease is { } release ? PlanContentAsync(item, item.InstanceId, release) : Task.CompletedTask;
    }

    /// <summary>The row whose update runs in the instance. A reload keeps it, because the update reports to it, as it does for a mod.</summary>
    private GameSaveItem? RunningRecordedRow(Guid instanceId)
        => _runningUpdates.GetValueOrDefault(instanceId) switch
        {
            GameSaveItem { IsInstalling: true } row => row,
            UpdateAllItem { RecordedRow: { IsInstalling: true } row } => row,
            _ => null,
        };

    /// <summary>
    /// The newest release above the record that the saved channel offers and that the game of the instance can load, as the
    /// plan of a mod looks for one. Null also when the index cannot say or when a kept build is missing, which stops a plan.
    /// </summary>
    private async Task<ModVersionMetadata?> NewerRecordedReleaseAsync(BoreaServices services, Instance instance, InstalledContentRecord record)
    {
        try
        {
            var game = PlanningGameVersion(services, instance);
            var releases = await services.UserContent.GetReleaseHistoryAsync(record.ListingId);
            return releases.FirstOrDefault(release => release.Version.CompareTo(record.Version) > 0
                && release.IsOffered
                && services.Settings.ReleaseChannel.Includes(release.ReleaseStatus)
                && Compatibility.Evaluate(release, game) != GameCompatibility.Incompatible);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The rows of the shown instance that offer a newer release. Other rows of the sections can still belong to the instance shown before.</summary>
    private IEnumerable<GameSaveItem> RecordedUpdates(Guid? instanceId)
        => VehiclesSection.Items.Concat(SavesSection.Items).Where(item => item.InstanceId == instanceId && item.NewerRelease is not null);

    /// <summary>The Home card counts what the instance page offers, and that page has no row for a record whose folder is gone.</summary>
    private async Task<int> RecordedUpdateCountAsync(BoreaServices services, Instance? instance)
    {
        if (instance is null)
            return 0;

        var count = 0;
        foreach (var record in await InstalledContentOfAsync(instance.InstanceId))
        {
            if (await NewerRecordedReleaseAsync(services, instance, record) is not null && await HasRecordedFolderAsync(services, instance.InstanceId, record))
                count++;
        }

        return count;
    }

    /// <summary>A folder that cannot be read counts as there, so a count is never lost over a read that failed.</summary>
    private static Task<bool> HasRecordedFolderAsync(BoreaServices services, Guid instanceId, InstalledContentRecord record)
        => Task.Run(() =>
        {
            try
            {
                // a row matches its record in any letter case, and so does the install
                var kind = record.Type == ContentType.Save ? GameSaveKind.Save : GameSaveKind.Vehicle;
                var folder = services.GameSaves.GetFolder(instanceId, kind);
                return Directory.Exists(folder) && Directory.EnumerateDirectories(folder).Any(path => string.Equals(Path.GetFileName(path), record.FolderName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        });

    /// <summary>The lines of the modal of "Update all" for what follows the mods, with the notes of the release each one goes to. A row that waits plans again after the mods, so it has a line too.</summary>
    internal IReadOnlyList<UpdateChangeItem> RecordedUpdateChanges(Guid instanceId)
        => RecordedUpdates(instanceId)
            .Select(item => new UpdateChangeItem(
                Localization,
                item.NewerRelease!.ModId,
                item.Name,
                item.Record!.Version,
                item.NewerRelease.Version,
                ReleaseChangelog.From(item.NewerRelease, $"{item.Name} {item.NewerRelease.Version}", Localization.ContentChangelog) is { } changelog ? [changelog] : []))
            .OrderBy(change => change.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Plans the vehicles and saves after the mods of "Update all" ended without a stop or an error, one by one and each
    /// the way its own button does, so a row that asks waits for its own go. A finished update builds new rows, so a row
    /// that waits plans once more after the last one ran, and an error is shown again on the new row of the same item.
    /// A stop on a row ends the run, as a stop in the mods does.
    /// </summary>
    private async Task UpdateRecordedContentAfterModsAsync(UpdateAllItem item, bool modsRan)
    {
        if (!modsRan || item.InstallError is not null || item.ProgressStatus is not null)
            return;

        await RunUpdateAsync(item, item.InstanceId, async () =>
        {
            var seen = new HashSet<string>(ModIds.Comparer);
            var waiting = new HashSet<string>(ModIds.Comparer);
            var errors = new Dictionary<string, string>(ModIds.Comparer);
            var stopped = false;
            while (!stopped && RecordedUpdates(item.InstanceId).FirstOrDefault(row => row.CanUpdate && !seen.Contains(row.Record!.ListingId)) is { } row)
            {
                var listingId = row.Record!.ListingId;
                seen.Add(listingId);
                item.RecordedRow = row;
                await PlanRecordedUpdateAsync(row);
                item.RecordedRow = null;
                stopped = row.ProgressStatus is not null;
                if (row.IsConfirmingInstall)
                    waiting.Add(listingId);
                else if (row.InstallError is { } error)
                    errors[listingId] = error;
            }

            foreach (var row in RecordedUpdates(item.InstanceId).ToList())
            {
                if (!stopped && waiting.Contains(row.Record!.ListingId) && row.CanUpdate)
                    await PlanRecordedUpdateAsync(row);
                else if (errors.TryGetValue(row.Record!.ListingId, out var error))
                    row.InstallError = error;
            }

            return false;
        });
    }

    /// <summary>
    /// Removes a recorded row through the code of the CLI, which keeps the folder in the backups. A folder the player
    /// changed since the confirmation asks once more with the files it found.
    /// </summary>
    private async Task RemoveRecordedGameSaveAsync(GameSaveItem item, InstalledContentRecord record)
    {
        var asksAgain = false;
        var removed = await RunGameSaveActionAsync([item.InstanceId], () => Localization.FormatToastDeleteFailed(item.Name), async services =>
        {
            // a preview that closed can still read this folder
            await WhenVehiclePreviewReadsEndedAsync();
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

    /// <summary>A newer release in the saved channel than the recorded one that the game can load, or null.</summary>
    internal ModVersionMetadata? NewerRelease { get; private set; }

    public bool HasRecord => Record is not null;

    public string? ListingName => Listing?.Name;

    /// <summary>The link to the listing, which says what it does when the row already shows the same name.</summary>
    public string? ListingLinkText => string.Equals(ListingName, Name, StringComparison.OrdinalIgnoreCase) ? _owner.Localization.GameSaveViewPage : ListingName;

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
    public string DeleteConfirmText => Changes is { } changes ? _owner.Localization.FormatGameSaveDeleteChanged(Name, _owner.ShortChangedFilesText(changes)) : _owner.Localization.GameSaveDeleteConfirm;

    public UserContentPlan? PendingContentPlan { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(ShowsShareButton))]
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
    [NotifyPropertyChangedFor(nameof(ShowsShareButton))]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    [NotifyPropertyChangedFor(nameof(AddedModsText))]
    private InstallPlan? _pendingPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(ShowsShareButton))]
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
        OnPropertyChanged(nameof(ShowsShareButton));
        OnPropertyChanged(nameof(ShowsListingLine));
        OnPropertyChanged(nameof(ListingName));
        OnPropertyChanged(nameof(ListingLinkText));
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(NewerText));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(CanUpdate));
    }

    private void RefreshRecordText()
    {
        OnPropertyChanged(nameof(ListingLinkText));
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
    private Task UpdateAsync() => _owner.UpdateRecordedContentAsync(this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmRecordedUpdateAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);
}
