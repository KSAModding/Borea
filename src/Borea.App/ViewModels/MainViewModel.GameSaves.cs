using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Sharing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Vehicles and Saves sections of the instance page.
/// </summary>
public partial class MainViewModel
{
    private GameSaveSection? _vehiclesSection;
    private GameSaveSection? _savesSection;
    private IReadOnlyList<GameSaveSection> _sectionsWithProfileItems = [];

    public GameSaveSection VehiclesSection => _vehiclesSection ??= new GameSaveSection(this, GameSaveKind.Vehicle);

    public GameSaveSection SavesSection => _savesSection ??= new GameSaveSection(this, GameSaveKind.Save);

    public string? GameProfileInfoText => _services is null ? null : Localization.FormatGameSaveProfileInfo(WithoutUserProfile(_services.Paths.GetSharedProfileRoot()));

    public bool ShowInstanceStartsEmpty => _sectionsWithProfileItems.Count > 0;

    private async Task LoadGameSavesAsync()
    {
        VehiclesSection.Reset();
        SavesSection.Reset();
        _sectionsWithProfileItems = [];
        OnPropertyChanged(nameof(ShowInstanceStartsEmpty));
        OnPropertyChanged(nameof(GameProfileInfoText));
        await LoadGameSavesAsync(VehiclesSection);
        await LoadGameSavesAsync(SavesSection);
        await RefreshInstanceStartsEmptyAsync();
        if (ShareSchemaLoad is null && _services is { } services && services.ListingValidator.SchemaOrigin != ListingSchemaOrigin.Downloaded)
            ShareSchemaLoad = LoadShareSchemaAsync(services);
    }

    /// <summary>The one load of the newest schema in a session, whose types decide whether Share shows. Null until an instance page opens.</summary>
    internal Task? ShareSchemaLoad { get; private set; }

    /// <summary>
    /// Share shows only for a type that the index accepts, so it appears once the index lifts the rollout gate of the type,
    /// without a new Borea. The page does not wait for this.
    /// </summary>
    private async Task LoadShareSchemaAsync(BoreaServices services)
    {
        try
        {
            await services.ListingValidator.LoadSchemaAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
        }

        VehiclesSection.RefreshShare();
        SavesSection.RefreshShare();
    }

    internal async Task ShareGameSaveAsync(GameSaveItem item)
    {
        if (_services is not { } services)
            return;

        item.CloseEdits();
        if (services.Launcher.IsRunning(item.InstanceId))
        {
            ShowErrorToast(() => Localization.FormatToastShareFailed(item.Name), Localization.GameSaveCloseGame);
            return;
        }

        try
        {
            var content = await services.ContentIds.ReadAsync(item.Entry.Path, item.Entry.Kind);

            // the index checks the name the way the game names a new save, which an item from an older build can fail
            if (!GameSaveName.IsValid(content.Name))
            {
                BeginRenameGameSave(item);
                if (GameSaveName.Sanitize(content.Name) is { Length: > 0 } sanitized)
                    ModalInstanceName = sanitized;
                GameSaveRenameReason = Localization.FormatGameSaveShareRename(content.Name);
                return;
            }

            var providers = await services.ContentProviders.ReadAsync(item.InstanceId);
            var installed = (await services.Instances.GetByIdAsync(item.InstanceId))?.Mods ?? [];
            var share = new GameSaveShareStart(item.InstanceId, item.Entry, content, ContentShareProposal.Of(content.Ids, providers, installed));
            await OpenListingAsync();
            ListingEditor.StartShare(share);
        }
        catch (GameFolderException exception)
        {
            // the file name first, because the toast cuts a long text, and the full path in the details
            ShowErrorToast(() => Localization.FormatToastShareFailed(item.Name), $"{FolderProblemText(exception)}\n{exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowErrorToast(() => Localization.FormatToastShareFailed(item.Name), exception.Message);
        }
    }

    private string FolderProblemText(GameFolderException exception)
    {
        var file = Path.GetFileName(exception.Path);
        return exception.Problem switch
        {
            GameFolderProblem.MetadataMissing or GameFolderProblem.GameFileMissing => Localization.FormatShareFolderFileMissing(file),
            GameFolderProblem.MetadataUnreadable => Localization.FormatShareFolderNotToml(file),
            GameFolderProblem.NameMissing => Localization.FormatShareFolderNoName(file),
            _ => Localization.FormatShareFolderNotXml(file),
        };
    }

    /// <summary>The path relative to the instance or the game folder, because a full path fills several lines of a note.</summary>
    internal string ShortSharePath(Guid instanceId, string path)
    {
        if (_services is not { } services)
            return path;

        foreach (var root in new[] { services.Paths.GetInstanceRoot(instanceId), services.Paths.GetGameDirectoryPath() })
        {
            if (root is null)
                continue;

            var relative = Path.GetRelativePath(root, path);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        return path;
    }

    /// <summary>Lets the player pick the file, and writes the archive of the shared item into it. Null when nothing was saved.</summary>
    internal async Task<(string File, GameSaveArchive Archive)?> SaveShareArchiveAsync(GameSaveShareStart share)
    {
        if (_services is not { } services || WindowServices is not { } window)
            return null;

        var name = share.Content.Name;
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastShareFailed(name), Localization.LibraryFolderBusy);
            return null;
        }

        if (services.Launcher.IsRunning(share.InstanceId))
        {
            ShowErrorToast(() => Localization.FormatToastShareFailed(name), Localization.GameSaveCloseGame);
            return null;
        }

        try
        {
            GameSaveArchive? archive = null;
            var file = await window.SaveFileAsync(Localization.ListingShareSaveArchive, name + ".zip", "zip", Localization.ListingShareArchiveFileType,
                async stream => archive = await services.GameSaves.WriteShareArchiveAsync(share.InstanceId, share.Entry, name, stream));
            return file is not null && archive is not null ? (file, archive) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ShowErrorToast(() => Localization.FormatToastShareFailed(name), exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Writes the archive of the shared item into a new file in the temporary folder, which the caller deletes. Null when
    /// nothing was written, and a toast says why.
    /// </summary>
    internal async Task<(string File, GameSaveArchive Archive)?> WriteShareArchiveToTempAsync(GameSaveShareStart share)
    {
        if (_services is not { } services)
            return null;

        var name = share.Content.Name;
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastShareFailed(name), Localization.LibraryFolderBusy);
            return null;
        }

        if (services.Launcher.IsRunning(share.InstanceId))
        {
            ShowErrorToast(() => Localization.FormatToastShareFailed(name), Localization.GameSaveCloseGame);
            return null;
        }

        var file = Path.Combine(Path.GetTempPath(), "Borea-share-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return (file, await services.GameSaves.WriteShareArchiveAsync(share.InstanceId, share.Entry, name, stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            TryDeleteFile(file);
            ShowErrorToast(() => Localization.FormatToastShareFailed(name), exception.Message);
            return null;
        }
    }

    internal static void TryDeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task RefreshInstanceStartsEmptyAsync()
    {
        var sections = new List<GameSaveSection>();
        if (_services is { } services && SelectedInstance is { } instance && !VehiclesSection.HasItems && !SavesSection.HasItems
            && VehiclesSection.Error is null && SavesSection.Error is null)
        {
            try
            {
                foreach (var section in new[] { SavesSection, VehiclesSection })
                {
                    if (await services.GameSaves.HasSharedProfileItemsAsync(section.Kind))
                        sections.Add(section);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sections.Clear();
            }

            if (SelectedInstance?.InstanceId != instance.InstanceId)
                return;
        }

        _sectionsWithProfileItems = sections;
        OnPropertyChanged(nameof(ShowInstanceStartsEmpty));
    }

    [RelayCommand]
    private async Task CopyAllFromProfileAsync()
    {
        foreach (var section in _sectionsWithProfileItems)
            await BeginCopyFromProfileAsync(section);
    }

    private async Task LoadGameSavesAsync(GameSaveSection section)
    {
        if (_services is not { } services || SelectedInstance is not { } instance)
            return;

        try
        {
            var entries = await services.GameSaves.ListAsync(instance.InstanceId, section.Kind);
            if (SelectedInstance?.InstanceId != instance.InstanceId)
                return;

            var installed = GameVersionOf(services, instance.Instance);
            section.SetItems(entries.Select(entry => new GameSaveItem(this, section, instance.InstanceId, entry, installed)));
            await ShowRecordedContentAsync(section, instance.InstanceId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (SelectedInstance?.InstanceId == instance.InstanceId)
                section.Error = exception.Message;
        }
    }

    internal void OpenGameSaveFolder(string folder) => ShowOpenError(() => PathName(folder), TryOpenWithSystem(folder));

    internal void OpenGameSaveSectionFolder(GameSaveSection section)
    {
        if (_services is not { } services || SelectedInstance is not { } instance)
            return;

        // GameSaves and VehicleSaves create the folder in OnApplicationStart too, so creating it first changes nothing for the game
        OpenCreatingFolder(() => services.GameSaves.GetFolder(instance.InstanceId, section.Kind), () => section.Title);
    }

    internal Task BackUpGameSaveAsync(GameSaveItem item) => RunGameSaveActionAsync([item.InstanceId], () => Localization.FormatToastBackUpFailed(item.Name), async services =>
        {
            var zip = await services.GameSaves.BackUpAsync(item.InstanceId, item.Entry);
            return () => Localization.FormatGameSaveBackedUp(item.Name, zip);
        });

    internal async Task DeleteGameSaveAsync(GameSaveItem item)
    {
        item.CloseEdits();
        if (item.Record is { } record)
        {
            await RemoveRecordedGameSaveAsync(item, record);
            return;
        }

        var deleted = await RunGameSaveActionAsync([item.InstanceId], () => Localization.FormatToastDeleteFailed(item.Name), async services =>
        {
            await services.GameSaves.DeleteAsync(item.InstanceId, item.Entry);
            return () => Localization.FormatGameSaveDeleted(item.Name);
        });

        if (deleted)
        {
            await LoadGameSavesAsync(item.Section);
            await RefreshInstanceStartsEmptyAsync();
        }
    }

    internal void BeginRenameGameSave(GameSaveItem item)
    {
        item.CloseEdits();
        InstanceError = null;
        GameSaveRenameReason = null;
        ModalInstanceName = item.Name;
        RenamingGameSave = item;
    }

    /// <summary>Keeps the modal open with the reason when the rename is refused or fails.</summary>
    private async Task RenameGameSaveFromModalAsync(GameSaveItem item)
    {
        var name = ModalInstanceName.Trim();
        // a share reads meta.toml again, so its name can differ from the name of the list
        if (name == item.Name && name == item.FolderName && GameSaveRenameReason is null)
        {
            RenamingGameSave = null;
            return;
        }

        var error = await TryRenameGameSaveAsync(item, name);
        if (error is not null)
        {
            if (RenamingGameSave == item)
                InstanceError = error;
            else
                ShowErrorToast(() => Localization.FormatToastRenameFailed(item.Name), error);
            return;
        }

        if (RenamingGameSave == item)
            RenamingGameSave = null;
        ShowSuccessToast(() => Localization.FormatGameSaveRenamed(item.Name, name));
        await LoadGameSavesAsync(item.Section);
    }

    /// <summary>Null when the item has the new name.</summary>
    private async Task<string?> TryRenameGameSaveAsync(GameSaveItem item, string name)
    {
        if (_services is not { } services)
            return null;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
            return Localization.LibraryFolderBusy;

        // the game reads the list once at start and writes its next save under the name it read
        if (services.Launcher.IsRunning(item.InstanceId) || services.IsGameProcessRunning())
            return Localization.GameSaveCloseGame;

        try
        {
            return await services.GameSaves.RenameAsync(item.InstanceId, item.Entry, name) switch
            {
                GameSaveRenameOutcome.InvalidName => GameSaveName.Sanitize(name) is { Length: > 0 } sanitized
                    ? Localization.FormatGameSaveNameInvalid(sanitized)
                    : Localization.GameSaveNameNeedsLetter,
                GameSaveRenameOutcome.NameTaken => item.IsVehicle ? Localization.GameSaveVehicleNameTaken : Localization.GameSaveSaveNameTaken,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return exception.Message;
        }
    }

    internal void BeginCopyGameSave(GameSaveItem item)
    {
        item.CloseEdits();
        var targets = Instances.Where(instance => instance.InstanceId != item.InstanceId).ToList();
        if (targets.Count == 0)
        {
            ShowErrorToast(() => Localization.FormatToastCopyFailed(item.Name), Localization.GameSaveNoOtherInstance);
            return;
        }

        item.CopyTargets = targets;
        item.CopyTarget = targets[0];
        item.IsChoosingTarget = true;
    }

    internal Task CopyGameSaveAsync(GameSaveItem item, bool replace)
    {
        if (item.CopyTarget is not { } target)
            return Task.CompletedTask;

        return RunGameSaveActionAsync([item.InstanceId, target.InstanceId], () => Localization.FormatToastCopyFailed(item.Name), async services =>
        {
            var outcome = await services.GameSaves.CopyAsync(item.Entry, target.InstanceId, replace);
            item.CloseEdits();
            if (outcome == GameSaveCopyOutcome.Copied)
                return () => Localization.FormatGameSaveCopied(item.Name, target.Name);

            item.IsConfirmingReplace = true;
            return null;
        });
    }

    internal async Task BeginCopyFromProfileAsync(GameSaveSection section)
    {
        if (_services is not { } services)
            return;

        try
        {
            var entries = await services.GameSaves.ListSharedProfileAsync(section.Kind);
            // the profile items are offered for a copy into the shown instance, so they are compared with its build
            var installed = GameVersionOf(services, SelectedInstance?.Instance);
            section.ShowProfile(entries.Select(entry => new GameSaveItem(this, section, Guid.Empty, entry, installed)
            {
                ExistsInInstance = section.Items.Any(item => string.Equals(item.FolderName, entry.FolderName, StringComparison.OrdinalIgnoreCase)),
            }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowErrorToast(() => Localization.ToastCopyFromProfileFailed, exception.Message);
        }
    }

    internal async Task CopyFromProfileAsync(GameSaveSection section, bool replace)
    {
        if (SelectedInstance is not { } instance)
            return;

        var chosen = section.ProfileItems.Where(item => item.IsSelected).ToList();
        if (chosen.Count == 0)
            return;

        if (!replace && chosen.Any(item => item.ExistsInInstance))
        {
            section.IsConfirmingProfileReplace = true;
            return;
        }

        var copied = 0;
        var done = await RunGameSaveActionAsync([instance.InstanceId], () => Localization.ToastCopyFromProfileFailed, async services =>
        {
            foreach (var item in chosen)
            {
                if (await services.GameSaves.CopyAsync(item.Entry, instance.InstanceId, replace) == GameSaveCopyOutcome.Copied)
                    copied++;
            }

            return () => Localization.FormatGameSavesCopiedFromProfile(copied);
        });

        section.IsConfirmingProfileReplace = false;
        if (done)
            section.HideProfile();
        await LoadGameSavesAsync(section);
        await RefreshInstanceStartsEmptyAsync();
    }

    /// <summary>Backs up every save of the instance. Its page, when shown, turns to the tab with the saves.</summary>
    /// <param name="report">Shows the outcome in place of the toast, for a modal that the toast would draw under.</param>
    internal async Task BackUpAllSavesAsync(InstanceItem instance, GameSaveOutcomeReport? report = null)
    {
        if (CurrentWindowInstance && SelectedInstance?.InstanceId == instance.InstanceId)
            InstanceTab = InstanceTab.Content;

        await RunGameSaveActionAsync([instance.InstanceId], () => Localization.ToastBackUpAllFailed, async services =>
        {
            var zips = await services.GameSaves.BackUpAllAsync(instance.InstanceId, GameSaveKind.Save);
            return () => zips.Count == 0
                ? Localization.GameSavesNothingToBackUp
                : Localization.FormatGameSavesBackedUp(zips.Count, Path.GetDirectoryName(zips[0])!);
        }, report);
    }

    /// <summary>
    /// Shows the outcome as a toast, and "Close the game first" when Borea
    /// runs one of the instances.
    /// </summary>
    private async Task<bool> RunGameSaveActionAsync(IReadOnlyCollection<Guid> instanceIds, Func<string> failed, Func<BoreaServices, Task<Func<string>?>> action, GameSaveOutcomeReport? report = null)
    {
        if (_services is not { } services)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowFailure(Localization.LibraryFolderBusy);
            return false;
        }

        // the game opens a save file only while it writes it, so the change itself does not notice a running game
        if (instanceIds.Any(services.Launcher.IsRunning) || services.IsGameProcessRunning())
        {
            ShowFailure(Localization.GameSaveCloseGame);
            return false;
        }

        try
        {
            if (await action(services) is { } result && report?.Invoke(result, null) != true)
                ShowSuccessToast(result);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ShowFailure(exception.Message);
        }

        return false;

        void ShowFailure(string reason)
        {
            if (report?.Invoke(failed, reason) == true)
                services.Log.Write($"{failed()}: {reason}");
            else
                ShowErrorToast(failed, reason);
        }
    }

    private void RefreshGameSaveText()
    {
        OnPropertyChanged(nameof(GameProfileInfoText));
        VehiclesSection.RefreshText();
        SavesSection.RefreshText();
        RefreshBackupText();
    }
}

/// <summary>
/// Shows the outcome of a save action in place of its toast. The reason is null when the action worked.
/// Returns false when the place for the outcome is gone, so the toast shows it.
/// </summary>
internal delegate bool GameSaveOutcomeReport(Func<string> message, string? reason);

/// <summary>
/// The Vehicles or the Saves section of the instance page, with its chooser
/// for the game profile.
/// </summary>
public sealed partial class GameSaveSection : ObservableObject
{
    private readonly MainViewModel _owner;

    public GameSaveSection(MainViewModel owner, GameSaveKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    public GameSaveKind Kind { get; }

    public string Title => Kind == GameSaveKind.Vehicle ? _owner.Localization.InstanceGroupVehicles : _owner.Localization.InstanceGroupSaves;

    public string EmptyText => Kind == GameSaveKind.Vehicle ? _owner.Localization.GameSaveNoVehicles : _owner.Localization.GameSaveNoSaves;

    public string FindText => Kind == GameSaveKind.Vehicle ? _owner.Localization.GameSaveFindVehicles : _owner.Localization.GameSaveFindSaves;

    /// <summary>Whether the schema in use accepts the type, so the index takes a listing of it (RFC 0085 rollout gate).</summary>
    public bool CanShare => _owner.Services?.ListingValidator.AcceptedTypes.Contains(Kind == GameSaveKind.Vehicle ? ListingFolders.VehicleType : ListingFolders.SaveType) == true;

    /// <summary>A save does not list its mods, so Borea cannot check them on a copy.</summary>
    public string? CopyNoteText => Kind == GameSaveKind.Save ? _owner.Localization.GameSaveCopyModsNote : null;

    public ObservableCollection<GameSaveItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public ObservableCollection<GameSaveItem> ProfileItems { get; } = [];

    public bool HasProfileItems => ProfileItems.Count > 0;

    /// <summary>Null while only some of the items are selected, so the check box shows the mixed state.</summary>
    public bool? AreAllProfileItemsSelected => SelectedProfileItemCount == 0 ? false : SelectedProfileItemCount == ProfileItems.Count ? true : null;

    public string ProfileSelectionText => _owner.Localization.FormatGameSaveProfileSelected(SelectedProfileItemCount, ProfileItems.Count);

    private int SelectedProfileItemCount => ProfileItems.Count(item => item.IsSelected);

    /// <summary>Why the section could not be read.</summary>
    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isChoosingFromProfile;

    [ObservableProperty]
    private bool _isConfirmingProfileReplace;

    internal void Reset()
    {
        Error = null;
        HideProfile();
        SetItems([]);
    }

    internal void SetItems(IEnumerable<GameSaveItem> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(nameof(HasItems));
    }

    internal void ShowProfile(IEnumerable<GameSaveItem> items)
    {
        ProfileItems.Clear();
        foreach (var item in items)
            ProfileItems.Add(item);
        OnPropertyChanged(nameof(HasProfileItems));
        RefreshProfileSelection();
        IsConfirmingProfileReplace = false;
        IsChoosingFromProfile = true;
    }

    internal void HideProfile()
    {
        IsChoosingFromProfile = false;
        IsConfirmingProfileReplace = false;
        ProfileItems.Clear();
        OnPropertyChanged(nameof(HasProfileItems));
        RefreshProfileSelection();
    }

    /// <summary>Takes the chooser back to the normal copy state, because the replace question belongs to the selection that made it.</summary>
    internal void OnProfileSelectionChanged()
    {
        IsConfirmingProfileReplace = false;
        RefreshProfileSelection();
    }

    internal void RefreshProfileSelection()
    {
        OnPropertyChanged(nameof(AreAllProfileItemsSelected));
        OnPropertyChanged(nameof(ProfileSelectionText));
    }

    internal void RefreshShare()
    {
        OnPropertyChanged(nameof(CanShare));
        foreach (var item in Items)
            item.RefreshShare();
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(FindText));
        OnPropertyChanged(nameof(CopyNoteText));
        OnPropertyChanged(nameof(ProfileSelectionText));
        foreach (var item in Items.Concat(ProfileItems))
            item.RefreshText();
    }

    [RelayCommand]
    private void OpenFolder() => _owner.OpenGameSaveSectionFolder(this);

    [RelayCommand]
    private void FindOnDiscover() => _owner.OpenDiscoverTab(Kind == GameSaveKind.Vehicle ? ContentType.Vehicle : ContentType.Save);

    [RelayCommand]
    private Task BeginCopyFromProfileAsync() => _owner.BeginCopyFromProfileAsync(this);

    [RelayCommand]
    private Task CopyFromProfileAsync() => _owner.CopyFromProfileAsync(this, replace: false);

    [RelayCommand]
    private Task ReplaceFromProfileAsync() => _owner.CopyFromProfileAsync(this, replace: true);

    /// <summary>Selects the items that are not selected yet, and clears them all when every item is selected.</summary>
    [RelayCommand]
    private void ToggleAllProfileItems()
    {
        var select = AreAllProfileItemsSelected != true;
        foreach (var item in ProfileItems)
            item.IsSelected = select;
    }

    [RelayCommand]
    private void CancelCopyFromProfile() => HideProfile();
}

/// <summary>
/// One save or vehicle folder, in a section or in the chooser for the game profile.
/// </summary>
public sealed partial class GameSaveItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly GameVersion? _build;
    private readonly GameVersion? _installed;

    public GameSaveItem(MainViewModel owner, GameSaveSection section, Guid instanceId, GameSaveEntry entry, GameVersion? installed)
    {
        _owner = owner;
        Section = section;
        InstanceId = instanceId;
        Entry = entry;
        _build = GameVersion.TryParse(entry.GameBuild, out var build) ? build : null;
        _installed = installed;
    }

    internal GameSaveSection Section { get; }

    /// <summary>Empty for a folder of the game profile.</summary>
    internal Guid InstanceId { get; }

    internal GameSaveEntry Entry { get; }

    public string Name => Entry.Name;

    public string FolderName => Entry.FolderName;

    public bool IsVehicle => Entry.Kind == GameSaveKind.Vehicle;

    public string UpdatedText => _owner.Localization.FormatGameSaveUpdated(MainViewModel.DateTimeText(Entry.UpdatedAt));

    public string? BuildText => _build?.ToString() ?? Entry.GameBuild;

    public string? BuildToolTip => BuildText is { } build ? _owner.Localization.FormatGameSaveBuild(build) : null;

    public bool IsOlderBuild => _build is { } build && _installed is { } installed && build < installed;

    public string? OlderBuildText => IsOlderBuild ? _owner.Localization.FormatGameSaveOlderBuild(BuildText!, _installed!.Value.ToString()) : null;

    public string SizeText => _owner.FormatGameDataSize(Entry.SizeBytes);

    public string? CopyNoteText => Section.CopyNoteText;

    public bool ExistsInInstance { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => Section.OnProfileSelectionChanged();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isChoosingTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isConfirmingReplace;

    [ObservableProperty]
    private IReadOnlyList<InstanceItem> _copyTargets = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplaceText))]
    private InstanceItem? _copyTarget;

    public string? ReplaceText => CopyTarget is null ? null : _owner.Localization.FormatGameSaveReplace(Name, CopyTarget.Name);

    public bool IsIdle => !IsConfirmingDelete && !IsChoosingTarget && !IsConfirmingReplace && !IsConfirmingInstall && !IsInstalling;

    /// <summary>An item of an instance, of a type that the index accepts.</summary>
    public bool CanShare => InstanceId != Guid.Empty && Section.CanShare;

    internal void RefreshShare()
    {
        OnPropertyChanged(nameof(CanShare));
        ShareCommand.NotifyCanExecuteChanged();
    }

    internal void CloseEdits()
    {
        IsConfirmingDelete = false;
        IsChoosingTarget = false;
        IsConfirmingReplace = false;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(BuildToolTip));
        OnPropertyChanged(nameof(OlderBuildText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(CopyNoteText));
        OnPropertyChanged(nameof(ReplaceText));
        RefreshRecordText();
    }

    [RelayCommand]
    private void OpenFolder() => _owner.OpenGameSaveFolder(Entry.Path);

    [RelayCommand]
    private Task BackUpAsync() => _owner.BackUpGameSaveAsync(this);

    [RelayCommand]
    private void BeginCopy() => _owner.BeginCopyGameSave(this);

    [RelayCommand]
    private void BeginRename() => _owner.BeginRenameGameSave(this);

    [RelayCommand(CanExecute = nameof(CanShare))]
    private Task ShareAsync() => _owner.ShareGameSaveAsync(this);

    [RelayCommand]
    private Task CopyAsync() => _owner.CopyGameSaveAsync(this, replace: false);

    [RelayCommand]
    private Task ConfirmReplaceAsync() => _owner.CopyGameSaveAsync(this, replace: true);

    [RelayCommand]
    private void BeginDelete()
    {
        CloseEdits();
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private Task ConfirmDeleteAsync() => _owner.DeleteGameSaveAsync(this);

    [RelayCommand]
    private void Cancel() => CloseEdits();
}
