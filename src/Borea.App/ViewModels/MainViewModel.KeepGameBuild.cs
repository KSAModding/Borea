using System;
using System.IO;
using System.Threading.Tasks;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// "Keep this game build" in the instance menu: a modal that says what the copy needs, and the copy as a task in the
/// Tasks drawer.
/// </summary>
public partial class MainViewModel
{
    private InstanceItem? _keepGameBuildItem;

    /// <summary>What keeping the build would do for the row of the open modal. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeepGameBuildModalOpen), nameof(KeepGameBuildText), nameof(KeepGameBuildFreeSpaceText), nameof(KeepGameBuildRefusalText), nameof(CanConfirmKeepGameBuild))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmKeepGameBuildCommand))]
    private GameBuildKeepCheck? _keepGameBuildCheck;

    public bool IsKeepGameBuildModalOpen => KeepGameBuildCheck is not null;

    public bool CanConfirmKeepGameBuild => KeepGameBuildCheck?.Refusal == GameBuildKeepRefusal.None;

    public string? KeepGameBuildText => KeepGameBuildCheck is { Refusal: GameBuildKeepRefusal.None, Version: { } version, Folder: { } folder } check && _keepGameBuildItem is { } item
        ? check.BytesToCopy > 0
            ? Localization.FormatModalKeepGameBuildText(version.ToString(), SizeText(check.BytesToCopy), folder, item.Name)
            : Localization.FormatModalKeepGameBuildShared(version.ToString(), folder, item.Name)
        : null;

    public string? KeepGameBuildFreeSpaceText => KeepGameBuildCheck is { Refusal: GameBuildKeepRefusal.None, BytesToCopy: > 0, FreeBytes: { } free }
        ? Localization.FormatModalKeepGameBuildFreeSpace(SizeText(free))
        : null;

    public string? KeepGameBuildRefusalText => KeepGameBuildCheck is { } check ? KeepGameBuildRefusal(check) : null;

    internal async Task BeginKeepGameBuildAsync(InstanceItem item)
    {
        if (_services is not { } services)
            return;

        GameBuildKeepCheck check;
        try
        {
            check = await Task.Run(() => services.GameBuilds.Check(item.Instance));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowErrorToast(() => Localization.InstanceKeepGameBuild, exception.Message);
            return;
        }

        _keepGameBuildItem = item;
        KeepGameBuildCheck = check;
    }

    [RelayCommand]
    private void CancelKeepGameBuild()
    {
        KeepGameBuildCheck = null;
        _keepGameBuildItem = null;
    }

    // a keep for another instance can start while a copy runs, and the keeper runs the keeps one at a time
    [RelayCommand(CanExecute = nameof(CanConfirmKeepGameBuild), AllowConcurrentExecutions = true)]
    private async Task ConfirmKeepGameBuildAsync()
    {
        if (_services is not { } services || _keepGameBuildItem is not { } item || KeepGameBuildCheck is not { Refusal: GameBuildKeepRefusal.None, Version: { } version })
            return;

        CancelKeepGameBuild();
        var build = version.ToString();
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastGameBuildKeepFailed(build), Localization.LibraryFolderBusy);
            return;
        }

        var run = StartInstallRun(StartTask(TaskKind.GameBuildKeep, build, item.InstanceId));
        var task = run.TaskItem;
        var progress = new Progress<LibraryMoveProgress>(value => run.ShowReport(() => task.Report(
            Localization.FormatLibraryFolderCopying(value.Files, value.TotalFiles, InstallProgressText.Number(value.Bytes), InstallProgressText.Number(value.TotalBytes)),
            value.PercentComplete)));
        var completed = false;
        var stopped = false;
        string? error = null;
        var logText = $"Instance {item.InstanceId}: kept KSA {build}.";
        try
        {
            var result = await Task.Run(() => services.GameBuilds.KeepAsync(item.InstanceId, version, progress, run.InstallStop.Token));

            // a second keep of the same instance, started while the row did not show the first one yet, finds it done
            completed = result.Refusal == GameBuildKeepRefusal.None
                || (result.Refusal == GameBuildKeepRefusal.AlreadyKept && result.Version == version);
            if (!completed)
            {
                error = KeepGameBuildRefusal(result);
                logText = $"Instance {item.InstanceId}: KSA {build} was not kept, refused as {result.Refusal}.";
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            logText = $"Instance {item.InstanceId}: the copy of KSA {build} was stopped.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = exception.Message;
            logText = $"Instance {item.InstanceId}: KSA {build} was not kept. {exception.Message}";
        }
        finally
        {
            EndInstallRun(run, completed, stopped, error);
        }

        services.Log.Write(logText);
        await ReloadInstancesAsync();
    }

    private string? KeepGameBuildRefusal(GameBuildKeepCheck check) => check.Refusal switch
    {
        GameBuildKeepRefusal.AlreadyKept => Localization.FormatKeepGameBuildAlreadyKept(check.Version?.ToString() ?? string.Empty),
        GameBuildKeepRefusal.NoGame => Localization.KeepGameBuildNoGame,
        GameBuildKeepRefusal.WineWrapper => Localization.KeepGameBuildWineWrapper,
        GameBuildKeepRefusal.GameRunning => Localization.GameSaveCloseGame,
        GameBuildKeepRefusal.FolderTaken => Localization.FormatKeepGameBuildFolderTaken(check.Folder ?? string.Empty),
        GameBuildKeepRefusal.LibraryInsideGame => Localization.KeepGameBuildLibraryInsideGame,
        GameBuildKeepRefusal.NotEnoughSpace => Localization.FormatKeepGameBuildNotEnoughSpace(SizeText(check.BytesToCopy), SizeText(check.FreeBytes ?? 0)),
        GameBuildKeepRefusal.BuildChanged => Localization.FormatKeepGameBuildBuildChanged(check.Version?.ToString() ?? string.Empty),
        _ => null,
    };

    private void RefreshKeepGameBuildText()
    {
        OnPropertyChanged(nameof(KeepGameBuildText));
        OnPropertyChanged(nameof(KeepGameBuildFreeSpaceText));
        OnPropertyChanged(nameof(KeepGameBuildRefusalText));
    }
}
