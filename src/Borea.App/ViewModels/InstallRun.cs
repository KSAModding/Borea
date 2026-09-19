using System;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// One running install. Its Stop button and a closing window request the same
/// <see cref="InstallStop"/>, and its Pause button pauses the download there.
/// </summary>
public sealed partial class InstallRun : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _reportGate = new();
    private bool _hasEnded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StopText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private bool _isStopping;

    /// <summary>True while the running mod is past its download, so a stop waits until that mod is finished.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StopText))]
    private bool _isFinishingMod;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseText))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private bool _isPaused;

    internal InstallRun(LocalizationService localization, TaskItem taskItem)
    {
        _localization = localization;
        TaskItem = taskItem;
    }

    internal InstallStop InstallStop { get; } = new();

    internal TaskItem TaskItem { get; }

    internal Task Ended => _ended.Task;

    /// <summary>Shows the last paused report again, because a paused download sends no new text after a language change.</summary>
    internal Action? RepeatPausedReport { get; set; }

    public string StopText => !IsStopping ? _localization.InstallStop
        : IsFinishingMod ? _localization.InstallStoppingAfterMod
        : _localization.InstallStopping;

    public string PauseText => IsPaused ? _localization.InstallResume : _localization.InstallPause;

    [RelayCommand(CanExecute = nameof(CanStop))]
    internal void Stop()
    {
        IsStopping = true;
        IsPaused = false;
        InstallStop.Request();
    }

    private bool CanStop() => !IsStopping;

    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    internal void TogglePause()
    {
        if (IsPaused)
        {
            InstallStop.Resume();
            IsPaused = false;
        }
        else
        {
            IsPaused = InstallStop.Pause();
        }
    }

    private bool CanTogglePause() => !IsStopping && (IsPaused || IsDownloading);

    internal void Report(InstallPhase phase)
    {
        IsDownloading = phase == InstallPhase.Downloading;
        IsFinishingMod = !IsDownloading;
        if (!IsDownloading)
            IsPaused = false;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(StopText));
        OnPropertyChanged(nameof(PauseText));
        RepeatPausedReport?.Invoke();
    }

    /// <summary>
    /// Shows a progress report unless the run has ended. Without a UI thread,
    /// <see cref="Progress{T}"/> can deliver a report while the run ends, so
    /// the report and <see cref="End"/> never interleave.
    /// </summary>
    internal void ShowReport(Action show)
    {
        lock (_reportGate)
        {
            if (!_hasEnded)
                show();
        }
    }

    internal void End()
    {
        lock (_reportGate)
            _hasEnded = true;
        _ended.TrySetResult();
    }
}
