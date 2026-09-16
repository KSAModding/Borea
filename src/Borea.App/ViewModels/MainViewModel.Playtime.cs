using System;
using System.IO;
using System.Threading.Tasks;
using Borea.Core.Instances;

namespace Borea.App.ViewModels;

/// <summary>
/// How long the shown instance was played, in the header of the instance page.
/// </summary>
public partial class MainViewModel
{
    private (Guid InstanceId, InstancePlaytime Playtime)? _instancePlaytime;

    private Task _playtimeLoad = Task.CompletedTask;

    private InstancePlaytime? ShownPlaytime
        => _instancePlaytime is { } read && read.InstanceId == SelectedInstance?.InstanceId ? read.Playtime : null;

    /// <summary>Null until the logs of the shown instance are read.</summary>
    public string? InstancePlaytimeText => ShownPlaytime switch
    {
        null => null,
        { IsKnown: false } => Localization.InstancePlaytimeUnknown,
        { Sessions: 0 } => Localization.InstanceNoPlaytime,
        { IncludesRunningSession: true } playtime => Localization.FormatInstancePlayedRunning(Localization.FormatDuration(playtime.Total)),
        var playtime => Localization.FormatInstancePlayed(Localization.FormatDuration(playtime.Total)),
    };

    public string InstancePlaytimeToolTip => ShownPlaytime is { IsKnown: false }
        ? Localization.InstancePlaytimeUnknownToolTip
        : Localization.InstancePlaytimeToolTip;

    public string? InstanceSessionsText => ShownPlaytime is { IsKnown: true, Sessions: > 0 } playtime
        ? Localization.FormatInstanceSessions(playtime.Sessions)
        : null;

    /// <summary>Completes when the playtime of the last opened instance is read.</summary>
    internal Task WhenPlaytimeLoadedAsync() => _playtimeLoad;

    /// <summary>Reads in the background, because the first read of an old instance goes through every archived log.</summary>
    private void StartPlaytimeLoad(Guid instanceId)
    {
        RefreshPlaytimeText();
        var previous = _playtimeLoad;
        var load = LoadPlaytimeAsync(instanceId);
        _playtimeLoad = previous.IsCompleted ? load : Task.WhenAll(previous, load);
    }

    private async Task LoadPlaytimeAsync(Guid instanceId)
    {
        if (_services is not { } services)
            return;

        InstancePlaytime playtime;
        try
        {
            playtime = await services.Playtime.GetPlaytimeAsync(instanceId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            playtime = new InstancePlaytime(TimeSpan.Zero, 0, IncludesRunningSession: false, UnreadableLogs: 0, IsKnown: false);
        }

        if (SelectedInstance?.InstanceId != instanceId)
            return;

        _instancePlaytime = (instanceId, playtime);
        RefreshPlaytimeText();
    }

    private void RefreshPlaytimeText()
    {
        OnPropertyChanged(nameof(InstancePlaytimeText));
        OnPropertyChanged(nameof(InstancePlaytimeToolTip));
        OnPropertyChanged(nameof(InstanceSessionsText));
    }
}
