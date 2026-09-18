using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.History;
using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The library folder in the General settings: where the Instances and Backups
/// folders are. A change moves them and rebuilds the services, like a change of
/// the game folder.
/// </summary>
public partial class MainViewModel
{
    private CancellationTokenSource? _libraryFolderCancellation;
    private TaskCompletionSource? _libraryFolderChangeEnded;
    private int _libraryUses;

    /// <summary>Null without services.</summary>
    public string? LibraryFolder => _services is null ? null : Path.GetDirectoryName(_services.Paths.GetInstancesRoot());

    public bool IsDefaultLibraryFolder => _services?.Settings.LibraryFolderPath is null;

    [ObservableProperty]
    private bool _isChangingLibraryFolder;

    [ObservableProperty]
    private bool _canCancelLibraryFolderChange;

    [ObservableProperty]
    private double _libraryFolderProgress;

    [ObservableProperty]
    private string? _libraryFolderProgressText;

    [ObservableProperty]
    private string? _libraryFolderMessage;

    [ObservableProperty]
    private string? _libraryFolderError;

    /// <summary>
    /// Counts work that writes to the library until the result is disposed,
    /// so the library folder does not change under it. Null while it changes.
    /// </summary>
    private IDisposable? TryUseLibrary()
    {
        if (IsChangingLibraryFolder)
            return null;

        Interlocked.Increment(ref _libraryUses);
        return new LibraryUse(this);
    }

    /// <param name="folder">The chosen folder, or null for Borea's own folder.</param>
    [RelayCommand]
    internal async Task ChangeLibraryFolderAsync(string? folder)
    {
        if (_services is not { } services || IsChangingLibraryFolder || IsClosing)
            return;

        LibraryFolderMessage = null;
        LibraryFolderError = null;

        // every attempt shows in the task history, also one that Borea refuses
        var task = StartTask(TaskKind.LibraryFolderChange, WithoutUserProfile(folder ?? BoreaFolder ?? string.Empty));
        if (IsSetupBusy || Volatile.Read(ref _libraryUses) > 0)
        {
            LibraryFolderError = Localization.LibraryFolderWaitForTask;
            Tasks.End(task, TaskState.Failed, LibraryFolderError);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _libraryFolderCancellation = cancellation;
        _libraryFolderChangeEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IsChangingLibraryFolder = true;
        LibraryFolderProgressText = Localization.LibraryFolderMoving;
        task.Report(LibraryFolderProgressText, null);
        var progress = new Progress<LibraryMoveProgress>(value => ShowLibraryMoveProgress(value, task));
        var state = TaskState.Failed;
        try
        {
            LibraryFolderChangeResult result;
            try
            {
                result = await Task.Run(() => services.LibraryFolderChanger.ChangeAsync(folder, progress, cancellation.Token));
            }
            catch (OperationCanceledException)
            {
                LibraryFolderMessage = Localization.LibraryFolderCancelled;
                state = TaskState.Stopped;
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException)
            {
                LibraryFolderError = Localization.FormatLibraryFolderFailed(exception.Message);
                return;
            }

            CanCancelLibraryFolderChange = false;
            if (!result.Changed)
            {
                LibraryFolderError = LibraryFolderResultText(result);
                return;
            }

            // the folder changed, so a failed reload below shows in the settings and not as a failed change
            state = TaskState.Finished;
            LibraryFolderMessage = LibraryFolderResultText(result);
            try
            {
                await RebuildServicesAsync();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or HttpRequestException or TaskCanceledException)
            {
                LibraryFolderError = Localization.FormatLibraryFolderReloadFailed(result.Folder, exception.Message);
            }
        }
        finally
        {
            Tasks.End(task, state, state == TaskState.Failed ? LibraryFolderError : null);
            _libraryFolderCancellation = null;
            IsChangingLibraryFolder = false;
            CanCancelLibraryFolderChange = false;
            LibraryFolderProgress = 0;
            LibraryFolderProgressText = null;
            OnPropertyChanged(nameof(LibraryFolder));
            OnPropertyChanged(nameof(IsDefaultLibraryFolder));
            var ended = _libraryFolderChangeEnded;
            _libraryFolderChangeEnded = null;
            ended?.TrySetResult();
        }
    }

    /// <summary>
    /// Cancels a running library folder change and returns once it ended. The
    /// changer stops only a copy that Borea has not saved as the new folder yet
    /// and removes that copy, so a change past that point runs to its end.
    /// </summary>
    internal async Task StopLibraryFolderChangeAsync()
    {
        while (_libraryFolderChangeEnded is { } ended)
        {
            _libraryFolderCancellation?.Cancel();
            await ended.Task;
        }
    }

    [RelayCommand]
    private Task UseDefaultLibraryFolderAsync() => ChangeLibraryFolderAsync(null);

    [RelayCommand]
    private void CancelLibraryFolderChange() => _libraryFolderCancellation?.Cancel();

    [RelayCommand]
    private void OpenLibraryFolder()
    {
        if (LibraryFolder is not { } folder)
            return;

        LibraryFolderMessage = null;
        LibraryFolderError = TryOpenWithSystem(folder);
    }

    private void ShowLibraryMoveProgress(LibraryMoveProgress value, TaskItem task)
    {
        if (!IsChangingLibraryFolder)
            return;

        var copying = value.Stage == LibraryMoveStage.Copying;
        CanCancelLibraryFolderChange = copying;
        LibraryFolderProgress = value.PercentComplete;
        LibraryFolderProgressText = copying
            ? Localization.FormatLibraryFolderCopying(value.Files, value.TotalFiles, InstallProgressText.Number(value.Bytes), InstallProgressText.Number(value.TotalBytes))
            : Localization.LibraryFolderRemovingOldFiles;
        task.Report(LibraryFolderProgressText, copying ? LibraryFolderProgress : null);
    }

    private string LibraryFolderResultText(LibraryFolderChangeResult result) => result.Outcome switch
    {
        LibraryFolderChangeOutcome.Moved when result.OldFilesRemain => Localization.FormatLibraryFolderMovedOldFilesRemain(result.Folder, result.PreviousFolder),
        LibraryFolderChangeOutcome.Moved => Localization.FormatLibraryFolderMoved(result.Folder),
        LibraryFolderChangeOutcome.Adopted => Localization.FormatLibraryFolderAdopted(result.Folder),
        LibraryFolderChangeOutcome.NotAbsolute => Localization.FormatLibraryFolderNotAbsolute(result.Folder),
        LibraryFolderChangeOutcome.IsFile => Localization.FormatLibraryFolderIsFile(result.Folder),
        LibraryFolderChangeOutcome.CurrentLibrary => Localization.LibraryFolderCurrent,
        LibraryFolderChangeOutcome.InsideCurrentLibrary => Localization.FormatLibraryFolderInsideCurrent(result.PreviousFolder),
        LibraryFolderChangeOutcome.ContainsCurrentLibrary => Localization.FormatLibraryFolderContainsCurrent(result.PreviousFolder),
        LibraryFolderChangeOutcome.InsideBoreaFolder => Localization.FormatLibraryFolderInsideBoreaFolder(BoreaFolder ?? result.PreviousFolder),
        LibraryFolderChangeOutcome.ContainsBoreaFolder => Localization.FormatLibraryFolderContainsBoreaFolder(BoreaFolder ?? result.PreviousFolder),
        LibraryFolderChangeOutcome.InsideGameDirectory => Localization.LibraryFolderInsideGame,
        LibraryFolderChangeOutcome.InsideSharedProfile => Localization.LibraryFolderInsideProfile,
        LibraryFolderChangeOutcome.NotWritable => Localization.FormatLibraryFolderNotWritable(result.Folder),
        LibraryFolderChangeOutcome.TargetNotEmpty => Localization.FormatLibraryFolderTargetNotEmpty(result.Folder),
        LibraryFolderChangeOutcome.BothHaveInstances => Localization.FormatLibraryFolderBothHaveInstances(result.PreviousFolder, result.Folder),
        LibraryFolderChangeOutcome.GameRunning => Localization.LibraryFolderGameRunning,
        LibraryFolderChangeOutcome.BoreaRunning => Localization.LibraryFolderBoreaRunning,
        LibraryFolderChangeOutcome.InstanceBusy => Localization.LibraryFolderInstanceBusy,
        LibraryFolderChangeOutcome.FileLocked => Localization.FormatLibraryFolderFileLocked(result.LockedFile ?? result.PreviousFolder),
        _ => result.Message,
    };

    private sealed class LibraryUse(MainViewModel owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref owner._libraryUses);
        }
    }
}
