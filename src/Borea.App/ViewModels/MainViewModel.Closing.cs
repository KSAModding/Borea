using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.History;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// Closing the window: running installs and a library folder change stop and
/// the task history is saved first. A second close request while Borea waits
/// offers to close at once.
/// </summary>
public partial class MainViewModel
{
    private Action? _closeWindow;
    private bool _isClosingNow;

    [ObservableProperty]
    private bool _isCloseNowOpen;

    /// <summary>The tasks the close waits for, in the order they started.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloseWaitingForHistory))]
    [NotifyPropertyChangedFor(nameof(CloseStopsInstall))]
    private IReadOnlyList<TaskItem> _closeWaitsFor = [];

    public bool IsCloseWaitingForHistory => CloseWaitsFor.Count == 0;

    public bool CloseStopsInstall => CloseWaitsFor.Any(task => task.Run is not null);

    /// <summary>Ends the App at once.</summary>
    internal Action? EndApp { get; set; }

    /// <summary>
    /// Returns whether the window may close now. Otherwise the first request
    /// stops the work and calls <paramref name="closeWindow"/> once it ended,
    /// and a later request opens the Close now modal.
    /// </summary>
    internal bool RequestClose(Action closeWindow)
    {
        if (_isClosingNow || !MustWaitToClose())
            return true;

        if (_closeWindow is null)
        {
            _closeWindow = closeWindow;
            _ = CloseAfterTasksAsync();
        }
        else
        {
            RefreshCloseWaitsFor();
            IsCloseNowOpen = true;
        }

        return false;
    }

    private bool MustWaitToClose()
        => HasRunningInstalls || IsChangingLibraryFolder || !Tasks.WhenSavedAsync().IsCompleted;

    private async Task CloseAfterTasksAsync()
    {
        Tasks.Ended += _ => RefreshCloseWaitsFor();
        do
        {
            await StopInstallsAsync();
            await StopLibraryFolderChangeAsync();
            await Tasks.WhenSavedAsync();
        }
        while (MustWaitToClose());

        IsCloseNowOpen = false;
        _closeWindow?.Invoke();
    }

    private void RefreshCloseWaitsFor()
        => CloseWaitsFor = Tasks.Running.Where(task => task.Run is not null || task.Kind == TaskKind.LibraryFolderChange).ToList();

    [RelayCommand]
    private void KeepWaiting() => IsCloseNowOpen = false;

    [RelayCommand]
    private void CloseNow()
    {
        RefreshCloseWaitsFor();
        var unfinished = CloseWaitsFor.Count == 0
            ? "the task history save"
            : string.Join(", ", CloseWaitsFor.Select(task => $"{task.Kind} {task.Subject}".TrimEnd()));
        _services?.Log.Write($"Closed at once before these tasks ended: {unfinished}.");
        _isClosingNow = true;
        IsCloseNowOpen = false;
        EndApp?.Invoke();
    }
}
