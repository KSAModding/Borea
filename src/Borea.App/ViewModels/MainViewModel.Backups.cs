using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.History;
using Borea.Core.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Backups section of the Game data tab, and the General setting that
/// deletes old backups.
/// </summary>
public partial class MainViewModel
{
    private static readonly int[] BackupRetentionChoices = [30, 90, 365];

    private IReadOnlyList<BackupRetentionOption>? _backupRetentionOptions;

    private bool _backupRetentionChanged;

    private int? _backupRetentionDays;

    private Task _backupCleanup = Task.CompletedTask;

    private bool _isLoadingBackups;

    public ObservableCollection<GameSaveBackupItem> BackupItems { get; } = [];

    public bool HasBackups => BackupItems.Count > 0;

    public bool ShowNoBackups => !_isLoadingBackups && !HasBackups && BackupsError is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoBackups))]
    private string? _backupsError;

    private int? BackupRetentionDays => _backupRetentionChanged ? _backupRetentionDays : _appPreferences.BackupRetentionDays;

    /// <summary>Off, the fixed choices, and a saved value that is not one of them.</summary>
    public IReadOnlyList<BackupRetentionOption> BackupRetentionOptions => _backupRetentionOptions ??= BuildBackupRetentionOptions();

    public BackupRetentionOption SelectedBackupRetention
    {
        get => BackupRetentionOptions.First(option => option.Days == BackupRetentionDays);
        set
        {
            if (value is null || value.Days == BackupRetentionDays)
                return;

            _backupRetentionDays = value.Days;
            _backupRetentionChanged = true;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithBackupRetentionDays(value.Days));
            StartBackupCleanup();
        }
    }

    private BackupRetentionOption[] BuildBackupRetentionOptions()
    {
        var choices = new List<int>(BackupRetentionChoices);
        if (_appPreferences.BackupRetentionDays is { } saved && !choices.Contains(saved))
            choices.Add(saved);

        return [new BackupRetentionOption(Localization, null), .. choices.Order().Select(days => new BackupRetentionOption(Localization, days))];
    }

    /// <summary>Completes when the deletion of old backups that runs now has finished.</summary>
    internal Task WhenBackupsCleanedAsync() => _backupCleanup;

    private void StartBackupCleanup() => _backupCleanup = CleanUpBackupsAsync(_backupCleanup);

    private async Task CleanUpBackupsAsync(Task previous)
    {
        await Task.WhenAny(previous);
        if (_services is not { } services || BackupRetentionDays is not { } days)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            services.Log.Write($"The backups older than {days} days were not deleted, because the library folder is in use.");
            return;
        }

        int deleted;
        try
        {
            deleted = await services.GameSaveBackups.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-days));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            services.Log.Write($"The backups older than {days} days were not deleted.", exception);
            return;
        }

        services.Log.Write($"Deleted {deleted} backups older than {days} days.");
        if (deleted > 0 && IsGameDataTab)
            await LoadBackupsAsync();
    }

    private async Task LoadBackupsAsync()
    {
        BackupsError = null;
        SetBackupItems([]);
        if (_services is not { } services || SelectedInstance is not { } instance)
            return;

        SetLoadingBackups(true);
        try
        {
            var backups = await services.GameSaveBackups.ListAsync(instance.InstanceId);
            if (SelectedInstance?.InstanceId == instance.InstanceId)
                SetBackupItems(backups.Select(backup => new GameSaveBackupItem(this, backup, instance.Name)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (SelectedInstance?.InstanceId == instance.InstanceId)
                BackupsError = exception.Message;
        }
        finally
        {
            SetLoadingBackups(false);
        }
    }

    private void SetLoadingBackups(bool loading)
    {
        _isLoadingBackups = loading;
        OnPropertyChanged(nameof(ShowNoBackups));
    }

    private void SetBackupItems(IEnumerable<GameSaveBackupItem> items)
    {
        BackupItems.Clear();
        foreach (var item in items)
            BackupItems.Add(item);
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(ShowNoBackups));
    }

    internal Task RestoreBackupAsync(GameSaveBackupItem item, bool replace)
        => RunBackupTaskAsync(item, TaskKind.BackupRestore, () => Localization.FormatToastBackupRestoreFailed(item.Name), async services =>
        {
            if (await services.GameSaveBackups.RestoreAsync(item.Backup, replace) == GameSaveRestoreOutcome.Restored)
            {
                await LoadGameSavesAsync(item.Backup.Kind == GameSaveKind.Vehicle ? VehiclesSection : SavesSection);
                return true;
            }

            item.IsConfirmingReplace = true;
            return false;
        });

    internal Task DeleteBackupAsync(GameSaveBackupItem item)
        => RunBackupTaskAsync(item, TaskKind.BackupDelete, () => Localization.FormatToastBackupDeleteFailed(item.Name), async services =>
        {
            await services.GameSaveBackups.DeleteAsync(item.Backup);
            return true;
        });

    /// <summary>A run that only asks before it replaces leaves no task in the history.</summary>
    private async Task RunBackupTaskAsync(GameSaveBackupItem item, TaskKind kind, Func<string> failed, Func<BoreaServices, Task<bool>> action)
    {
        if (_services is not { } services)
            return;

        item.CloseEdits();
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(failed, Localization.LibraryFolderBusy);
            return;
        }

        var task = StartTask(kind, item.Name, item.Backup.InstanceId);
        var completed = false;
        string? error = null;
        try
        {
            // the game opens a save file only while it writes it, so the change itself does not notice a running game
            if (kind == TaskKind.BackupRestore && services.Launcher.IsRunning(item.Backup.InstanceId))
                error = Localization.GameSaveCloseGame;
            else
                completed = await action(services);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            error = exception.Message;
        }
        finally
        {
            EndTask(task, completed, stopped: false, error);
        }

        if (completed || error is not null)
            await LoadBackupsAsync();
    }

    private void RefreshBackupText()
    {
        foreach (var item in BackupItems)
            item.RefreshText();
        foreach (var option in BackupRetentionOptions)
            option.RefreshText();
    }
}

/// <summary>One choice of the General setting that deletes old backups. Null days is Off.</summary>
public sealed class BackupRetentionOption : ObservableObject
{
    private readonly LocalizationService _localization;

    public BackupRetentionOption(LocalizationService localization, int? days)
    {
        _localization = localization;
        Days = days;
    }

    public int? Days { get; }

    public string Text => Days is { } days ? _localization.FormatBackupRetentionDays(days) : _localization.BackupRetentionOff;

    internal void RefreshText() => OnPropertyChanged(nameof(Text));
}

/// <summary>
/// One backup of a save or vehicle on the Game data tab.
/// </summary>
public sealed partial class GameSaveBackupItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly string _instanceName;

    public GameSaveBackupItem(MainViewModel owner, GameSaveBackup backup, string instanceName)
    {
        _owner = owner;
        Backup = backup;
        _instanceName = instanceName;
    }

    internal GameSaveBackup Backup { get; }

    public string Name => Backup.Name;

    public string Id => Backup.Id;

    public bool IsVehicle => Backup.Kind == GameSaveKind.Vehicle;

    public bool IsSave => Backup.Kind == GameSaveKind.Save;

    public bool CanRestore => Backup.CanRestore;

    public string TimeText
    {
        get
        {
            var time = Backup.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            return Backup.Reason switch
            {
                GameSaveBackupReason.BackedUp => _owner.Localization.FormatBackupBackedUp(time),
                GameSaveBackupReason.Deleted => _owner.Localization.FormatBackupDeleted(time),
                GameSaveBackupReason.Replaced => _owner.Localization.FormatBackupReplaced(time),
                _ => _owner.Localization.FormatBackupMoved(time),
            };
        }
    }

    public string SizeText => _owner.FormatGameDataSize(Backup.SizeBytes);

    public string ReplaceText => _owner.Localization.FormatGameSaveReplace(Backup.FolderName ?? Name, _instanceName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isConfirmingReplace;

    public bool IsIdle => !IsConfirmingDelete && !IsConfirmingReplace;

    internal void CloseEdits()
    {
        IsConfirmingDelete = false;
        IsConfirmingReplace = false;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ReplaceText));
    }

    [RelayCommand]
    private Task RestoreAsync() => _owner.RestoreBackupAsync(this, replace: false);

    [RelayCommand]
    private Task ConfirmReplaceAsync() => _owner.RestoreBackupAsync(this, replace: true);

    [RelayCommand]
    private void BeginDelete()
    {
        CloseEdits();
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private Task ConfirmDeleteAsync() => _owner.DeleteBackupAsync(this);

    [RelayCommand]
    private void Cancel() => CloseEdits();
}
