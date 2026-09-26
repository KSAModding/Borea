using System;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.History;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum ToastKind
{
    Success,
    Error,
}

/// <summary>The one action a short result offers, such as the page that resolves it.</summary>
internal sealed record ToastAction(Func<string> Text, Action Run);

/// <summary>
/// One toast about a task that ended or about a short result. It closes by itself
/// after its time, which starts again once neither the pointer nor the keyboard focus is on it.
/// </summary>
public sealed partial class ToastItem : ObservableObject
{
    private readonly ToastService _owner;
    private readonly Func<string>? _message;
    private readonly string? _detail;
    private readonly ToastAction? _action;
    private readonly TaskState _messageState;
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

    internal ToastItem(ToastService owner, ToastKind kind, Func<string> message, string? detail, ToastAction? action = null)
    {
        _owner = owner;
        _message = message;
        _detail = detail;
        _action = action;
        _messageState = kind == ToastKind.Error ? TaskState.Failed : TaskState.Finished;
    }

    /// <summary>The task the toast is about. Null for a toast about a short result.</summary>
    internal TaskItem? TaskItem { get; }

    private LocalizationService Localization => _owner.Localization;

    private TaskState State => TaskItem?.State ?? _messageState;

    private string Subject => TaskItem?.Subject ?? string.Empty;

    private string InstanceName => TaskItem?.InstanceName ?? string.Empty;

    public bool IsFinished => State == TaskState.Finished;

    public bool IsStopped => State == TaskState.Stopped;

    public bool IsFailed => State == TaskState.Failed;

    public string Message => TaskItem is not { } task ? _message!() : task.State switch
    {
        TaskState.Finished => FinishedText(task),
        TaskState.Stopped => StoppedText(task),
        _ => FailedText(task),
    };

    /// <summary>The reason of a failure, or how far a stopped install got.</summary>
    public string? Detail => TaskItem is not { } task ? _detail : task.State switch
    {
        TaskState.Failed => task.FailureReason,
        TaskState.Stopped when task.StoppedAfter is { Completed: > 0 } after => task.Kind is TaskKind.Update or TaskKind.UpdateAll or TaskKind.PackUpdate
            ? Localization.FormatToastStoppedUpdated(after.Completed, after.Total)
            : Localization.FormatToastStoppedInstalled(after.Completed, after.Total),
        _ => null,
    };

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    /// <summary>A toast with its own action points to what resolves it, so it has no details to show.</summary>
    public bool CanShowDetails => !IsFinished && !HasAction;

    public bool HasAction => _action is not null;

    public string? ActionText => _action?.Text();

    public bool HasActions => CanOpenInstance || CanShowDetails || HasAction;

    internal string AnnouncementText => HasDetail ? Message + Environment.NewLine + Detail : Message;

    internal TimeSpan Duration => IsFinished ? ToastService.FinishedDuration : ToastService.ProblemDuration;

    [RelayCommand]
    private Task OpenInstanceAsync() => _owner.OpenInstanceAsync(this);

    [RelayCommand]
    private void ShowDetails() => _owner.ShowDetails(this);

    [RelayCommand]
    private void RunAction()
    {
        _owner.Close(this);
        _action?.Run();
    }

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

    private string FinishedText(TaskItem task) => task.Kind switch
    {
        TaskKind.Update or TaskKind.PackUpdate when task.NewVersion is { } version => Localization.FormatToastUpdated(Subject, version, InstanceName),
        TaskKind.Update or TaskKind.UpdateAll => Localization.FormatToastUpdatedAll(task.ModCount, InstanceName),
        TaskKind.ModRemoval => Localization.FormatToastRemoved(Subject, InstanceName),
        TaskKind.LoaderInstall => Localization.FormatToastLoaderInstalled(Subject, task.NewVersion ?? string.Empty),
        TaskKind.ModListImport => Localization.FormatToastInstanceCreated(Subject),
        TaskKind.ManualReplace => Localization.FormatToastReplaced(Subject, InstanceName),
        TaskKind.ModHandover => Localization.FormatToastManaged(Subject, InstanceName),
        TaskKind.LibraryFolderChange => Localization.FormatToastLibraryFolderChanged(Subject),
        TaskKind.BackupRestore => Localization.FormatToastBackupRestored(Subject, InstanceName),
        TaskKind.BackupDelete => Localization.FormatToastBackupDeleted(Subject),
        _ => Localization.FormatToastAdded(Subject, InstanceName),
    };

    private string StoppedText(TaskItem task) => task.Kind switch
    {
        TaskKind.Update or TaskKind.PackUpdate => Localization.FormatToastUpdateStopped(Subject),
        TaskKind.UpdateAll => Localization.FormatToastUpdateAllStopped(InstanceName),
        TaskKind.LibraryFolderChange => Localization.ToastLibraryFolderStopped,
        _ => Localization.FormatToastInstallStopped(Subject),
    };

    private string FailedText(TaskItem task) => task.Kind switch
    {
        TaskKind.IndexRefresh => Localization.ToastIndexRefreshFailed,
        TaskKind.Update or TaskKind.PackUpdate => Localization.FormatToastUpdateFailed(Subject),
        TaskKind.UpdateAll => Localization.FormatToastUpdateAllFailed(InstanceName),
        TaskKind.ModRemoval => Localization.FormatToastRemoveFailed(Subject),
        TaskKind.ModListImport => Localization.FormatToastCreateInstanceFailed(Subject),
        TaskKind.ManualReplace => Localization.FormatToastReplaceFailed(Subject),
        TaskKind.ModHandover => Localization.FormatToastManageFailed(Subject),
        TaskKind.LibraryFolderChange => Localization.FormatToastLibraryFolderFailed(Subject),
        TaskKind.BackupRestore => Localization.FormatToastBackupRestoreFailed(Subject),
        TaskKind.BackupDelete => Localization.FormatToastBackupDeleteFailed(Subject),
        TaskKind.BoreaUpdate => Localization.FormatToastSelfUpdateFailed(task.Version ?? string.Empty),
        _ => Localization.FormatToastInstallFailed(Subject),
    };

    internal void RefreshCanOpenInstance() => CanOpenInstance = _owner.HasInstance(TaskItem?.InstanceId);

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(ActionText));
    }
}
