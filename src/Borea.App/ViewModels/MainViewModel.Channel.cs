using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Mods;
using Borea.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The release channel in the General settings. It is a Borea setting, so a
/// change saves the settings file and rebuilds the services.
/// </summary>
public partial class MainViewModel
{
    private IReadOnlyList<ReleaseChannelOption>? _releaseChannelOptions;

    private Task _releaseChannelSaves = Task.CompletedTask;

    public IReadOnlyList<ReleaseChannelOption> ReleaseChannelOptions
        => _releaseChannelOptions ??= Enum.GetValues<ReleaseChannel>().Select(channel => new ReleaseChannelOption(Localization, channel)).ToArray();

    private ReleaseChannel SavedReleaseChannel => _services?.Settings.ReleaseChannel ?? ReleaseChannel.Stable;

    /// <summary>
    /// A choice while the services are rebuilt, a loader is installed or the
    /// library folder changes is refused, because the rebuild disposes the
    /// services that work uses.
    /// </summary>
    public ReleaseChannelOption SelectedReleaseChannel
    {
        get => OptionFor(SavedReleaseChannel);
        set
        {
            if (value is null || _services is null || IsSetupBusy || IsChangingLibraryFolder)
            {
                OnPropertyChanged(nameof(SelectedReleaseChannel));
                return;
            }

            if (value.Channel != SavedReleaseChannel)
                _releaseChannelSaves = SaveReleaseChannelAsync(_services, value.Channel);
        }
    }

    /// <summary>Completes when the last channel change is saved.</summary>
    internal Task WhenReleaseChannelSavedAsync() => _releaseChannelSaves;

    internal ReleaseChannelOption OptionFor(ReleaseChannel channel)
        => ReleaseChannelOptions.First(option => option.Channel == channel);

    internal string ReleaseStatusText(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Stable => Localization.ReleaseStable,
        ReleaseStatus.Testing => Localization.ReleaseTesting,
        ReleaseStatus.Dev => Localization.ReleaseDev,
        _ => Localization.ReleaseUnknown,
    };

    private async Task SaveReleaseChannelAsync(BoreaServices services, ReleaseChannel channel)
    {
        IsSetupBusy = true;
        try
        {
            await services.SettingsRepository.SaveAsync((await ReadSavedSettingsAsync(services)).WithReleaseChannel(channel));
            PreferenceSaveError = null;
            await RebuildServicesAsync();
            VersionFilter = OptionFor(channel);
            if (CurrentWindowContent && SelectedContent is { } content)
                await LoadLatestVersionAsync(content);
            await RefreshLoaderStateAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            PreferenceSaveError = Localization.FormatPreferenceSaveError(exception.Message);
            OnPropertyChanged(nameof(SelectedReleaseChannel));
        }
        finally
        {
            IsSetupBusy = false;
        }
    }

    /// <summary>
    /// The settings as the file holds them now, so a change the CLI made while
    /// the App is open is kept.
    /// </summary>
    private static async Task<BoreaSettings> ReadSavedSettingsAsync(BoreaServices services)
        => await services.SettingsRepository.GetAsync() ?? new BoreaSettings(gameDirectoryPath: null);
}

/// <summary>One release channel, as the settings and the Show filter name it.</summary>
public sealed class ReleaseChannelOption : ObservableObject
{
    private readonly LocalizationService _localization;

    public ReleaseChannel Channel { get; }

    public string Text => Channel switch
    {
        ReleaseChannel.Testing => _localization.ReleaseChannelTesting,
        ReleaseChannel.Dev => _localization.ReleaseChannelDev,
        _ => _localization.ReleaseChannelStable,
    };

    public ReleaseChannelOption(LocalizationService localization, ReleaseChannel channel)
    {
        _localization = localization;
        Channel = channel;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(Text));
}
