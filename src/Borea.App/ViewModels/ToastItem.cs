using System;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.History;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// One toast about a task that ended. It closes by itself after its time,
/// which starts again once neither the pointer nor the keyboard focus is on it.
/// </summary>
public sealed partial class ToastItem : ObservableObject
{
    private readonly ToastService _owner;
    private CancellationTokenSource? _timer;
    private bool _isPointerOver;
    private bool _hasFocusWithin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActions))]
    private bool _canOpenInstance;

    internal ToastItem(ToastService owner, TaskItem taskItem)
    {
        _owner = owner;
        TaskItem = taskItem;
        _canOpenInstance = owner.HasInstance(taskItem.InstanceId);
    }

    internal TaskItem TaskItem { get; }

    private LocalizationService Localization => _owner.Localization;

    private string Subject => TaskItem.Subject ?? string.Empty;

    private string InstanceName => TaskItem.InstanceName ?? string.Empty;

    public bool IsFinished => TaskItem.State == TaskState.Finished;

    public bool IsStopped => TaskItem.State == TaskState.Stopped;

    public bool IsFailed => TaskItem.State == TaskState.Failed;

    public string Message => TaskItem.State switch
    {
        TaskState.Finished => FinishedText(),
        TaskState.Stopped => StoppedText(),
        _ => FailedText(),
    };

    /// <summary>The reason of a failure, or how far a stopped install got.</summary>
    public string? Detail => TaskItem.State switch
    {
        TaskState.Failed => TaskItem.FailureReason,
        TaskState.Stopped when TaskItem.StoppedAfter is { Completed: > 0 } after => TaskItem.Kind is TaskKind.Update or TaskKind.UpdateAll
            ? Localization.FormatToastStoppedUpdated(after.Completed, after.Total)
            : Localization.FormatToastStoppedInstalled(after.Completed, after.Total),
        _ => null,
    };

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public bool CanShowDetails => !IsFinished;

    public bool HasActions => CanOpenInstance || CanShowDetails;

    internal string AnnouncementText => HasDetail ? Message + Environment.NewLine + Detail : Message;

    internal TimeSpan Duration => IsFinished ? ToastService.FinishedDuration : ToastService.ProblemDuration;

    [RelayCommand]
    private Task OpenInstanceAsync() => _owner.OpenInstanceAsync(this);

    [RelayCommand]
    private void ShowDetails() => _owner.ShowDetails(this);

    [RelayCommand]
    private void Close() => _owner.Close(this);

    internal void SetPointerOver(bool value)
    {
        if (_isPointerOver == value)
            return;

        _isPointerOver = value;
        RestartTimer();
    }

    internal void SetFocusWithin(bool value)
    {
        if (_hasFocusWithin == value)
            return;

        _hasFocusWithin = value;
        RestartTimer();
    }

    internal void RestartTimer()
    {
        StopTimer();
        if (_isPointerOver || _hasFocusWithin || !_owner.Items.Contains(this))
            return;

        _timer = new CancellationTokenSource();
        _ = CloseAfterAsync(_timer.Token);
    }

    internal void StopTimer()
    {
        _timer?.Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    private async Task CloseAfterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(Duration, _owner.Clock, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
            _owner.Close(this);
    }

    private string FinishedText() => TaskItem.Kind switch
    {
        TaskKind.Update when TaskItem.NewVersion is { } version => Localization.FormatToastUpdated(Subject, version, InstanceName),
        TaskKind.Update or TaskKind.UpdateAll => Localization.FormatToastUpdatedAll(TaskItem.ModCount, InstanceName),
        TaskKind.ModRemoval => Localization.FormatToastRemoved(Subject, InstanceName),
        TaskKind.LoaderInstall => Localization.FormatToastLoaderInstalled(Subject, TaskItem.NewVersion ?? string.Empty),
        TaskKind.ModListImport => Localization.FormatToastInstanceCreated(Subject),
        TaskKind.ManualReplace => Localization.FormatToastReplaced(Subject, InstanceName),
        TaskKind.LibraryFolderChange => Localization.FormatToastLibraryFolderChanged(Subject),
        _ => Localization.FormatToastAdded(Subject, InstanceName),
    };

    private string StoppedText() => TaskItem.Kind switch
    {
        TaskKind.Update => Localization.FormatToastUpdateStopped(Subject),
        TaskKind.UpdateAll => Localization.FormatToastUpdateAllStopped(InstanceName),
        TaskKind.LibraryFolderChange => Localization.ToastLibraryFolderStopped,
        _ => Localization.FormatToastInstallStopped(Subject),
    };

    private string FailedText() => TaskItem.Kind switch
    {
        TaskKind.IndexRefresh => Localization.ToastIndexRefreshFailed,
        TaskKind.Update => Localization.FormatToastUpdateFailed(Subject),
        TaskKind.UpdateAll => Localization.FormatToastUpdateAllFailed(InstanceName),
        TaskKind.ModRemoval => Localization.FormatToastRemoveFailed(Subject),
        TaskKind.ModListImport => Localization.FormatToastCreateInstanceFailed(Subject),
        TaskKind.ManualReplace => Localization.FormatToastReplaceFailed(Subject),
        TaskKind.LibraryFolderChange => Localization.FormatToastLibraryFolderFailed(Subject),
        _ => Localization.FormatToastInstallFailed(Subject),
    };

    internal void RefreshCanOpenInstance() => CanOpenInstance = _owner.HasInstance(TaskItem.InstanceId);

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(Detail));
    }
}
