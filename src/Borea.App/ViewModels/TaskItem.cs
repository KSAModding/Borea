using System;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.History;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// One row of the Tasks page, a running or waiting task or one of the history.
/// </summary>
public sealed partial class TaskItem : ObservableObject
{
    private readonly TaskRegistry _registry;

    internal TaskKind Kind { get; }

    internal string? Subject { get; }

    internal Guid? InstanceId { get; private set; }

    public string? InstanceName { get; private set; }

    internal string? ContentId { get; }

    internal string? Version { get; }

    internal DateTimeOffset StartedAt { get; }

    /// <summary>How many mods the plan of a running install or update changes. It is not saved.</summary>
    internal int ModCount { get; private set; }

    /// <summary>The version an update or a loader install puts in. It is not saved.</summary>
    internal string? NewVersion { get; set; }

    /// <summary>How many of its mods a stopped install or update changed. It is not saved.</summary>
    internal (int Completed, int Total) StoppedAfter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StepText))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(HasRetry))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private TaskState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    [NotifyPropertyChangedFor(nameof(TimeToolTip))]
    private DateTimeOffset? _endedAt;

    [ObservableProperty]
    private string? _failureReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepText))]
    private string? _step;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _hasProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private InstallRun? _run;

    internal TaskItem(TaskRegistry registry, TaskKind kind, TaskState state, string? subject, Guid? instanceId, string? instanceName, string? contentId, string? version, DateTimeOffset startedAt)
    {
        _registry = registry;
        Kind = kind;
        _state = state;
        Subject = subject;
        InstanceId = instanceId;
        InstanceName = instanceName;
        ContentId = contentId;
        Version = version;
        StartedAt = startedAt;
    }

    internal static TaskItem FromEntry(TaskRegistry registry, TaskHistoryEntry entry)
        => new(registry, entry.Kind, entry.State, entry.Subject, entry.InstanceId, entry.InstanceName, entry.ContentId, entry.Version, entry.StartedAt)
        {
            EndedAt = entry.EndedAt,
            FailureReason = entry.FailureReason,
        };

    private LocalizationService Localization => _registry.Localization;

    public string Title => Kind switch
    {
        TaskKind.IndexRefresh => Localization.TaskIndexRefresh,
        TaskKind.Update or TaskKind.PackUpdate => Localization.FormatTaskUpdate(Subject ?? string.Empty),
        TaskKind.UpdateAll => Localization.TaskUpdateAll,
        TaskKind.ModRemoval => Localization.FormatTaskRemove(Subject ?? string.Empty),
        TaskKind.ModListImport => Localization.FormatTaskCreateInstance(Subject ?? string.Empty),
        TaskKind.ManualReplace => Localization.FormatTaskReplace(Subject ?? string.Empty),
        TaskKind.ModHandover => Localization.FormatTaskManage(Subject ?? string.Empty),
        TaskKind.LibraryFolderChange => Localization.FormatTaskLibraryFolder(Subject ?? string.Empty),
        TaskKind.BackupRestore => Localization.FormatTaskBackupRestore(Subject ?? string.Empty),
        TaskKind.BackupDelete => Localization.FormatTaskBackupDelete(Subject ?? string.Empty),
        TaskKind.LoaderInstall => Localization.FormatTaskInstall(LoaderInstallName),
        TaskKind.BoreaUpdate => Localization.FormatTaskSelfUpdate(Version ?? string.Empty),
        _ => Localization.FormatTaskInstall(Subject ?? string.Empty),
    };

    /// <summary>
    /// The loader name with the version it installs. The subject keeps the plain loader
    /// name because the toast of a finished install formats the version itself, so the
    /// title is the one place that joins the two.
    /// </summary>
    private string LoaderInstallName
    {
        get
        {
            var name = Subject ?? string.Empty;
            if (string.IsNullOrEmpty(Version))
                return name;

            return string.IsNullOrEmpty(name) ? Version : $"{name} {Version}";
        }
    }

    public string StateText => State switch
    {
        TaskState.Waiting => Localization.TaskWaiting,
        TaskState.Running => Localization.TaskRunning,
        TaskState.Paused => Localization.TaskPaused,
        TaskState.Finished => Localization.TaskFinished,
        TaskState.Stopped => Localization.TaskStopped,
        _ => Localization.TaskFailed,
    };

    public string StepText => Step ?? StateText;

    /// <summary>How long ago the task ended or started, such as "5m ago". The drawer refreshes it when it opens.</summary>
    public string TimeText => Localization.FormatTimeAgoShort(DateTimeOffset.UtcNow - (EndedAt ?? StartedAt));

    public string TimeToolTip => MainViewModel.DateTimeText(EndedAt ?? StartedAt);

    public bool IsFailed => State == TaskState.Failed;

    /// <summary>The rows that start a mod list import or a replace of a manual install have no Stop.</summary>
    public bool CanStop => Run is not null && Kind is not (TaskKind.ModListImport or TaskKind.ManualReplace);

    public bool HasRetry => State == TaskState.Failed && Kind switch
    {
        TaskKind.IndexRefresh => true,
        TaskKind.UpdateAll or TaskKind.PackUpdate => InstanceId is not null,
        TaskKind.ModInstall or TaskKind.PackInstall or TaskKind.Update => InstanceId is not null && ContentId is not null,
        _ => false,
    };

    public bool CanRetry => HasRetry && !_registry.IsBusyWith(this);

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => _registry.RetryAsync(this);

    /// <summary>The installs of one mod or pack share one row, and the updates of one instance run one at a time.</summary>
    internal bool DoesSameWorkAs(TaskItem other) => Kind is TaskKind.Update or TaskKind.UpdateAll or TaskKind.PackUpdate
        ? other.Kind is TaskKind.Update or TaskKind.UpdateAll or TaskKind.PackUpdate && other.InstanceId == InstanceId
        : other.Kind == Kind && ModIds.Equals(other.ContentId, ContentId);

    /// <summary>Names the instance that the task created.</summary>
    internal void SetInstance(Guid instanceId, string instanceName)
    {
        InstanceId = instanceId;
        InstanceName = instanceName;
        OnPropertyChanged(nameof(InstanceName));
    }

    internal void MarkRunning()
    {
        if (State == TaskState.Waiting)
            State = TaskState.Running;
    }

    /// <summary>Keeps how many mods the plan changes, and for an update the version it installs.</summary>
    internal void MarkRunning(InstallPlan plan)
    {
        MarkRunning();
        ModCount = plan.Operations.Count;
        if (Kind == TaskKind.Update && plan.Operations.FirstOrDefault(operation => ModIds.Equals(operation.Release.ModId, ContentId)) is { } update)
            NewVersion = update.Release.Version.ToString();
    }

    internal void Report(InstallProgressText text)
    {
        // IProgress delivers a report later, so the last one can arrive after End
        if (EndedAt is not null)
            return;

        if (State is TaskState.Waiting or TaskState.Running or TaskState.Paused)
            State = text.IsPaused ? TaskState.Paused : TaskState.Running;

        Step = text.Status;
        Detail = text.Detail;
        Progress = text.Percent;
        HasProgress = text.HasPercent;
    }

    /// <summary>The step of work that is not an install, with how far it is when that is known.</summary>
    internal void Report(string step, double? percent)
    {
        if (EndedAt is not null)
            return;

        MarkRunning();
        Step = step;
        Detail = null;
        Progress = percent ?? 0;
        HasProgress = percent is not null;
    }

    internal void End(TaskState state, string? failureReason, DateTimeOffset at)
    {
        State = state;
        FailureReason = failureReason;
        EndedAt = at;
        Step = null;
        Detail = null;
        Progress = 0;
        HasProgress = false;
        Run = null;
    }

    internal TaskHistoryEntry ToEntry()
        => new(Kind, State, Subject, InstanceId, InstanceName, StartedAt, EndedAt ?? StartedAt, FailureReason, ContentId, Version);

    internal void RefreshCanRetry()
    {
        OnPropertyChanged(nameof(CanRetry));
        RetryCommand.NotifyCanExecuteChanged();
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(TimeToolTip));
    }
}
