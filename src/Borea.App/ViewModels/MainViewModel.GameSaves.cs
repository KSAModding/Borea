using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
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

            var installed = services.InstalledVersion.GetInstalledVersion()?.Version;
            section.SetItems(entries.Select(entry => new GameSaveItem(this, section, instance.InstanceId, entry, installed)));
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
        if (_services is null || SelectedInstance is null)
            return;

        string? error;
        try
        {
            // GameSaves and VehicleSaves create the folder in OnApplicationStart too, so creating it first changes nothing for the game
            var folder = _services.GameSaves.GetFolder(SelectedInstance.InstanceId, section.Kind);
            Directory.CreateDirectory(folder);
            error = TryOpenWithSystem(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }

        ShowOpenError(() => section.Title, error);
    }

    internal Task BackUpGameSaveAsync(GameSaveItem item) => RunGameSaveActionAsync([item.InstanceId], () => Localization.FormatToastBackUpFailed(item.Name), async services =>
        {
            var zip = await services.GameSaves.BackUpAsync(item.InstanceId, item.Entry);
            return () => Localization.FormatGameSaveBackedUp(item.Name, zip);
        });

    internal async Task DeleteGameSaveAsync(GameSaveItem item)
    {
        item.CloseEdits();
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
            var installed = services.InstalledVersion.GetInstalledVersion()?.Version;
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

    [RelayCommand]
    private async Task BackUpAllSavesAsync()
    {
        if (SelectedInstance is not { } instance)
            return;

        InstanceTab = InstanceTab.Content;
        await RunGameSaveActionAsync([instance.InstanceId], () => Localization.ToastBackUpAllFailed, async services =>
        {
            var zips = await services.GameSaves.BackUpAllAsync(instance.InstanceId, GameSaveKind.Save);
            return () => zips.Count == 0
                ? Localization.GameSavesNothingToBackUp
                : Localization.FormatGameSavesBackedUp(zips.Count, Path.GetDirectoryName(zips[0])!);
        });
    }

    /// <summary>
    /// Shows the outcome as a toast, and "Close the game first" when Borea
    /// runs one of the instances or the game holds a file of the folder.
    /// </summary>
    private async Task<bool> RunGameSaveActionAsync(IReadOnlyCollection<Guid> instanceIds, Func<string> failed, Func<BoreaServices, Task<Func<string>?>> action)
    {
        if (_services is not { } services)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(failed, Localization.LibraryFolderBusy);
            return false;
        }

        // the game holds a save file open only while it writes it, so the file check alone misses a running game
        if (instanceIds.Any(services.Launcher.IsRunning))
        {
            ShowErrorToast(failed, Localization.GameSaveCloseGame);
            return false;
        }

        try
        {
            if (await action(services) is { } result)
                ShowSuccessToast(result);
            return true;
        }
        catch (GameSaveInUseException)
        {
            ShowErrorToast(failed, Localization.GameSaveCloseGame);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ShowErrorToast(failed, exception.Message);
        }

        return false;
    }

    private void RefreshGameSaveText()
    {
        OnPropertyChanged(nameof(GameProfileInfoText));
        VehiclesSection.RefreshText();
        SavesSection.RefreshText();
    }
}

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

    /// <summary>A save does not list its mods, so Borea cannot check them on a copy.</summary>
    public string? CopyNoteText => Kind == GameSaveKind.Save ? _owner.Localization.GameSaveCopyModsNote : null;

    public ObservableCollection<GameSaveItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public ObservableCollection<GameSaveItem> ProfileItems { get; } = [];

    public bool HasProfileItems => ProfileItems.Count > 0;

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
        IsConfirmingProfileReplace = false;
        IsChoosingFromProfile = true;
    }

    internal void HideProfile()
    {
        IsChoosingFromProfile = false;
        IsConfirmingProfileReplace = false;
        ProfileItems.Clear();
        OnPropertyChanged(nameof(HasProfileItems));
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(CopyNoteText));
        foreach (var item in Items.Concat(ProfileItems))
            item.RefreshText();
    }

    [RelayCommand]
    private void OpenFolder() => _owner.OpenGameSaveSectionFolder(this);

    [RelayCommand]
    private Task BeginCopyFromProfileAsync() => _owner.BeginCopyFromProfileAsync(this);

    [RelayCommand]
    private Task CopyFromProfileAsync() => _owner.CopyFromProfileAsync(this, replace: false);

    [RelayCommand]
    private Task ReplaceFromProfileAsync() => _owner.CopyFromProfileAsync(this, replace: true);

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

    public string UpdatedText => _owner.Localization.FormatGameSaveUpdated(Entry.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

    public string? BuildText => _build?.ToString() ?? Entry.GameBuild;

    public bool IsOlderBuild => _build is { } build && _installed is { } installed && build < installed;

    public string? OlderBuildText => IsOlderBuild ? _owner.Localization.FormatGameSaveOlderBuild(BuildText!, _installed!.Value.ToString()) : null;

    public string SizeText => _owner.FormatGameDataSize(Entry.SizeBytes);

    public string? CopyNoteText => Section.CopyNoteText;

    public bool ExistsInInstance { get; init; }

    [ObservableProperty]
    private bool _isSelected;

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

    public bool IsIdle => !IsConfirmingDelete && !IsChoosingTarget && !IsConfirmingReplace;

    internal void CloseEdits()
    {
        IsConfirmingDelete = false;
        IsChoosingTarget = false;
        IsConfirmingReplace = false;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(OlderBuildText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(CopyNoteText));
        OnPropertyChanged(nameof(ReplaceText));
    }

    [RelayCommand]
    private void OpenFolder() => _owner.OpenGameSaveFolder(Entry.Path);

    [RelayCommand]
    private Task BackUpAsync() => _owner.BackUpGameSaveAsync(this);

    [RelayCommand]
    private void BeginCopy() => _owner.BeginCopyGameSave(this);

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
