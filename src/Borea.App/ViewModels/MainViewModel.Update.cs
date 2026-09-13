using System;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>The notice that a newer Borea release exists, checked once per start.</summary>
public partial class MainViewModel
{
    private Task? _updateCheck;

    private bool? _checkForUpdatesAtStart;

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
            var release = await _services.ReleaseCheck.GetLatestReleaseAsync();
            if (release is not null && release.IsNewerThan(BoreaInformationalVersion))
                AvailableUpdate = release;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // a failed check shows nothing
        }
    }
}
