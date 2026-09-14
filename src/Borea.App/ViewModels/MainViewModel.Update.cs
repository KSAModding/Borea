using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>The notice that a newer Borea release exists, checked once per start.</summary>
public partial class MainViewModel
{
    private Task? _updateCheck;

    private bool? _checkForUpdatesAtStart;

    private BoreaUpdateChannel? _updateChannel;

    private IReadOnlyList<BoreaUpdateChannelOption>? _updateChannelOptions;

    /// <summary>The newer release, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdate))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateVersion))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateUrl))]
    private BoreaRelease? _availableUpdate;

    public bool HasAvailableUpdate => AvailableUpdate is not null;

    public string? AvailableUpdateVersion => AvailableUpdate?.Version.ToString();

    public string? AvailableUpdateUrl => AvailableUpdate?.PageUrl;

    /// <summary>The switch in the General settings. A change takes effect at the next start.</summary>
    public bool CheckForUpdatesAtStart
    {
        get => _checkForUpdatesAtStart ?? _appPreferences.CheckForUpdatesAtStart;
        set
        {
            if (value == CheckForUpdatesAtStart)
                return;

            _checkForUpdatesAtStart = value;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithCheckForUpdatesAtStart(value));
        }
    }

    public IReadOnlyList<BoreaUpdateChannelOption> UpdateChannelOptions
        => _updateChannelOptions ??= Enum.GetValues<BoreaUpdateChannel>().Select(channel => new BoreaUpdateChannelOption(Localization, channel)).ToArray();

    /// <summary>The update channel in the General settings. A change takes effect at the next start.</summary>
    public BoreaUpdateChannelOption SelectedUpdateChannel
    {
        get => UpdateChannelOptions.First(option => option.Channel == (_updateChannel ?? _appPreferences.UpdateChannel));
        set
        {
            if (value is null || value == SelectedUpdateChannel)
                return;

            _updateChannel = value.Channel;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithUpdateChannel(value.Channel));
        }
    }

    /// <summary>Starts the release check once, in the background.</summary>
    private void StartUpdateCheck() => _updateCheck ??= CheckForUpdateAsync();

    /// <summary>Completes when the release check has finished.</summary>
    internal Task WhenUpdateCheckedAsync() => _updateCheck ?? Task.CompletedTask;

    private async Task CheckForUpdateAsync()
    {
        if (_services is null || !_appPreferences.CheckForUpdatesAtStart)
            return;

        try
        {
            var release = await _services.ReleaseCheck.GetLatestReleaseAsync(_appPreferences.UpdateChannel);
            if (release is not null && release.IsNewerThan(BoreaInformationalVersion))
                AvailableUpdate = release;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // a failed check shows nothing
        }
    }
}

/// <summary>One Borea update channel, as the General settings name it.</summary>
public sealed class BoreaUpdateChannelOption : ObservableObject
{
    private readonly LocalizationService _localization;

    public BoreaUpdateChannel Channel { get; }

    public string Text => Channel switch
    {
        BoreaUpdateChannel.Testing => _localization.UpdateChannelTesting,
        BoreaUpdateChannel.Dev => _localization.UpdateChannelDev,
        _ => _localization.UpdateChannelStable,
    };

    public BoreaUpdateChannelOption(LocalizationService localization, BoreaUpdateChannel channel)
    {
        _localization = localization;
        Channel = channel;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(Text));
}
