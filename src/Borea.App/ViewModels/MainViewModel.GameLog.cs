using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Log tab of the instance page: the end of the log the game writes in the instance.
/// </summary>
public partial class MainViewModel
{
    private string? _gameLogPath;

    /// <summary>The last lines of the game log. Null when no log was read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameLog))]
    private string? _gameLogText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenGameLog))]
    private string? _gameLogPathText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenGameLog))]
    private bool _isGameLogMissing;

    [ObservableProperty]
    private string? _gameLogError;

    [ObservableProperty]
    private string? _gameLogMessage;

    public ObservableCollection<GameLogLine> GameLogLines { get; } = [];

    public bool HasGameLog => GameLogText is not null;

    public bool CanOpenGameLog => GameLogPathText is not null && !IsGameLogMissing;

    [RelayCommand]
    private async Task ShowInstanceLogAsync()
    {
        InstanceTab = InstanceTab.Log;
        await LoadGameLogAsync();
    }

    [RelayCommand]
    private Task ReloadGameLogAsync() => LoadGameLogAsync();

    private async Task LoadGameLogAsync()
    {
        GameLogError = null;
        GameLogMessage = null;
        GameLogText = null;
        GameLogLines.Clear();
        GameLogPathText = null;
        IsGameLogMissing = false;
        _gameLogPath = null;
        if (_services is null || SelectedInstance is null)
            return;

        var instanceId = SelectedInstance.InstanceId;
        _gameLogPath = _services.Paths.GetInstanceGameLogPath(instanceId);
        GameLogPathText = WithoutUserProfile(_gameLogPath);
        try
        {
            var log = await _services.GameLog.ReadGameLogAsync(instanceId);
            if (SelectedInstance?.InstanceId != instanceId)
                return;

            _gameLogPath = log.Path;
            GameLogPathText = WithoutUserProfile(log.Path);

            IsGameLogMissing = !log.Exists;
            if (!log.Exists)
                return;

            foreach (var line in GameLogLine.Parse(log.Lines))
                GameLogLines.Add(line);
            GameLogText = string.Join(Environment.NewLine, log.Lines);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (SelectedInstance?.InstanceId == instanceId)
                GameLogError = exception.Message;
        }
    }

    [RelayCommand]
    private void OpenGameLog()
    {
        if (_gameLogPath is not null)
            GameLogError = TryOpenWithSystem(_gameLogPath);
    }

    internal void ReportGameLogCopied()
    {
        GameLogError = null;
        GameLogMessage = Localization.AboutCopied;
    }
}
