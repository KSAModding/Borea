using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Mods;
using Borea.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The notice that a newer Borea release exists, checked once per start, and its release notes.</summary>
public partial class MainViewModel
{
    private Task? _updateCheck;

    private bool? _checkForUpdatesAtStart;

    private BoreaUpdateChannel? _updateChannel;

    private IReadOnlyList<BoreaUpdateChannelOption>? _updateChannelOptions;

    private IReadOnlyList<BoreaRelease> _releases = [];

    private ModVersion? _dismissedBoreaRelease;

    /// <summary>Whether this build may replace itself, read once when the check finds a release. Null while it is unknown.</summary>
    private SelfUpdateReadiness? _selfUpdateReadiness;

    /// <summary>
    /// Set while the new build takes the place of this one. A process that ends inside that step
    /// leaves the folder with no program file, so a close waits for it to end.
    /// </summary>
    private TaskCompletionSource? _selfUpdateInstall;

    /// <summary>Where the new build is started after the window closed. Null in the designer and in tests.</summary>
    internal PendingHandover? PendingHandover { get; init; }

    /// <summary>The newer release, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdate))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateVersion))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateText))]
    [NotifyPropertyChangedFor(nameof(AvailableUpdateUrl))]
    [NotifyPropertyChangedFor(nameof(ShowReleaseBanner))]
    [NotifyPropertyChangedFor(nameof(ReleaseBannerText))]
    [NotifyPropertyChangedFor(nameof(SelfUpdateActionText))]
    private BoreaRelease? _availableUpdate;

    /// <summary>What the self-update is doing, or why it stopped. Null while none has run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReleaseBannerText))]
    private string? _selfUpdateStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelfUpdateActionText))]
    private bool _isSelfUpdating;

    public bool HasAvailableUpdate => AvailableUpdate is not null;

    public string? AvailableUpdateVersion => AvailableUpdate?.Version.ToString();

    public string? AvailableUpdateText => AvailableUpdate is null ? null : $"{Localization.UpdateAvailable} {AvailableUpdateVersion}";

    public string? AvailableUpdateUrl => AvailableUpdate?.PageUrl;

    /// <summary>The Home banner names the release, why this build cannot replace itself, and the update work once it runs.</summary>
    public string? ReleaseBannerText
    {
        get
        {
            if (SelfUpdateStatus is { } status)
                return status;

            if (AvailableUpdateText is not { } text)
                return null;

            return _selfUpdateReadiness is { CanUpdate: false } block ? $"{text} {BlockText(block)}" : text;
        }
    }

    /// <summary>The "Update now" action of the Home banner, or null when this build cannot replace itself.</summary>
    public string? SelfUpdateActionText
        => AvailableUpdate is not null && !IsSelfUpdating && _selfUpdateReadiness is { CanUpdate: true } ? Localization.SelfUpdateNow : null;

    /// <summary>
    /// The Home banner shows until the player closes it, and it is back at the next start,
    /// because it holds the only "Update now".
    /// </summary>
    public bool ShowReleaseBanner => AvailableUpdate is { } release && !(_dismissedBoreaRelease >= release.Version);

    [ObservableProperty]
    private bool _isReleaseNotesOpen;

    /// <summary>The newer releases, newest first, and the installed one last.</summary>
    [ObservableProperty]
    private IReadOnlyList<BoreaReleaseNotesItem> _releaseNotes = [];

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
            _releases = await _services.ReleaseCheck.GetReleasesAsync(_appPreferences.UpdateChannel);
            if (_releases.FirstOrDefault() is { } newest && newest.IsNewerThan(BoreaInformationalVersion))
            {
                AvailableUpdate = newest;

                // The readiness reads files, so it is read here and not in the getters the banner binds to.
                _selfUpdateReadiness = await Task.Run(_services.SelfUpdater.GetReadiness);
                OnPropertyChanged(nameof(SelfUpdateActionText));
                OnPropertyChanged(nameof(ReleaseBannerText));
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // a failed check shows nothing
        }
    }

    /// <summary>
    /// Replaces this build with the release the banner offers. The download is checked against the
    /// checksums of the same release, the new build takes the place of this one while the window
    /// stands, and it starts once that window has closed.
    /// </summary>
    [RelayCommand]
    private async Task SelfUpdateAsync(CancellationToken cancellationToken)
    {
        if (_services is null || IsSelfUpdating || AvailableUpdate is not { } release)
            return;

        var readiness = Readiness();
        if (!readiness.CanUpdate)
        {
            SelfUpdateStatus = BlockText(readiness);
            return;
        }

        IsSelfUpdating = true;
        SelfUpdateStatus = Localization.FormatSelfUpdateDownloading(release.Version.ToString(), 0);
        StagedSelfUpdate? staged = null;
        try
        {
            var progress = new Progress<SelfUpdateProgress>(value => SelfUpdateStatus = ProgressText(release.Version, value));
            staged = await _services.SelfUpdater.StageAsync(release, progress, cancellationToken);

            // From here a close waits, because the folder holds no program file for a moment.
            _selfUpdateInstall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // The new build takes its place while the window stands, so the player reads what a failed step did.
            await Task.Run(staged.Install);
            if (PendingHandover is null)
            {
                SelfUpdateStatus = Localization.FormatSelfUpdateStartAgain(staged.Version.ToString());
                IsSelfUpdating = false;
                return;
            }

            SelfUpdateStatus = Localization.FormatSelfUpdateReady(staged.Version.ToString());
            PendingHandover.Run = staged.HandOver;
            PendingHandover.Describe = failure => FailureText(failure, staged);

            // The window may go now, and it has to go before the new build starts.
            EndSelfUpdateInstall();
            EndApp?.Invoke();
        }
        catch (SelfUpdateFailedException exception)
        {
            _services.Log.Write("The self-update stopped.", exception);
            SelfUpdateStatus = FailureText(exception, staged);
            IsSelfUpdating = false;
        }
        catch (OperationCanceledException)
        {
            SelfUpdateStatus = null;
            IsSelfUpdating = false;
        }
        finally
        {
            EndSelfUpdateInstall();
        }
    }

    /// <summary>Whether the new build is taking the place of this one right now.</summary>
    internal bool IsInstallingSelfUpdate => _selfUpdateInstall is not null;

    /// <summary>Completes when the new build is in place, and at once while none is being put in place.</summary>
    internal Task WhenSelfUpdateInstalledAsync() => _selfUpdateInstall?.Task ?? Task.CompletedTask;

    /// <summary>Lets a close go on, because the new build is in place or the update stopped.</summary>
    private void EndSelfUpdateInstall()
    {
        var install = _selfUpdateInstall;
        _selfUpdateInstall = null;
        install?.TrySetResult();
    }

    /// <summary>
    /// Read once, because the answer is about this build and cannot change while it runs. The banner
    /// takes the value the update check read, so this only fills it in when the command runs first.
    /// </summary>
    private SelfUpdateReadiness Readiness()
        => _selfUpdateReadiness ??= _services?.SelfUpdater.GetReadiness() ?? new SelfUpdateReadiness(SelfUpdateBlock.NotAReleaseBuild);

    private string ProgressText(ModVersion version, SelfUpdateProgress progress) => progress.Phase switch
    {
        SelfUpdatePhase.Verifying => Localization.FormatSelfUpdateVerifying(version.ToString()),
        SelfUpdatePhase.Unpacking => Localization.FormatSelfUpdateUnpacking(version.ToString()),
        _ => Localization.FormatSelfUpdateDownloading(version.ToString(), (int)progress.PercentComplete),
    };

    private string BlockText(SelfUpdateReadiness readiness) => readiness.Block switch
    {
        SelfUpdateBlock.PackageManaged when readiness.PackageManager is { } manager => Localization.FormatSelfUpdatePackageManaged(manager),
        SelfUpdateBlock.PackageManaged => Localization.SelfUpdatePackageManaged,
        SelfUpdateBlock.ReadOnlyLocation => Localization.SelfUpdateReadOnly,
        SelfUpdateBlock.UnsupportedPlatform => Localization.SelfUpdateUnsupportedPlatform,
        _ => Localization.SelfUpdateNotAReleaseBuild,
    };

    /// <summary>
    /// The failures up to the new build being in place. A new build that does not start is not one of
    /// them, because the window is gone by the time it is started, so that failure goes to the log.
    /// </summary>
    private string FailureText(SelfUpdateFailedException exception, StagedSelfUpdate? staged) => exception.Reason switch
    {
        SelfUpdateFailure.Blocked => BlockText(Readiness()),
        SelfUpdateFailure.NoArchive => Localization.SelfUpdateUnsupportedPlatform,
        SelfUpdateFailure.Download => Localization.SelfUpdateDownloadFailed,
        SelfUpdateFailure.Checksum => Localization.SelfUpdateChecksumFailed,
        SelfUpdateFailure.Unpack => Localization.SelfUpdateUnpackFailed,
        SelfUpdateFailure.Restore when staged is not null
            => Localization.FormatSelfUpdateRestoreFailed(Path.GetFileName(staged.ReplacedProgramPath), Path.GetFileName(staged.ProgramPath)),
        _ => Localization.SelfUpdateInstallFailed,
    };

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        var notes = _releases
            .Where(release => release.IsNewerThan(BoreaInformationalVersion))
            .Select(release => new BoreaReleaseNotesItem(release.Version, release, isInstalled: false))
            .ToList();
        if (ModVersion.TryParse(BoreaInformationalVersion, out var running))
        {
            var installed = _releases.FirstOrDefault(release => release.Version.CompareTo(running) == 0);
            notes.Add(new BoreaReleaseNotesItem(running, installed, isInstalled: true));
        }

        ReleaseNotes = notes;
        IsReleaseNotesOpen = true;
    }

    [RelayCommand]
    private void CloseReleaseNotes() => IsReleaseNotesOpen = false;

    [RelayCommand]
    private void DismissReleaseBanner()
    {
        if (AvailableUpdate is not { } release)
            return;

        _dismissedBoreaRelease = release.Version;
        OnPropertyChanged(nameof(ShowReleaseBanner));
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

/// <summary>One Borea release in the release notes. The installed version shows even when the check did not return it.</summary>
public sealed class BoreaReleaseNotesItem : ObservableObject
{
    private readonly BoreaRelease? _release;

    public string Version { get; }

    public bool IsInstalled { get; }

    public bool IsPreRelease { get; }

    public string? Notes => _release?.Notes;

    public bool HasNoNotes => _release is not null && _release.Notes is null;

    public string? PageUrl => IsInstalled ? null : _release?.PageUrl;

    public string? DateText => _release?.PublishedAt is { } at ? MainViewModel.DateText(at) : null;

    public BoreaReleaseNotesItem(ModVersion version, BoreaRelease? release, bool isInstalled)
    {
        _release = release;
        Version = version.ToString();
        IsInstalled = isInstalled;
        IsPreRelease = version.PreRelease is not null;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(DateText));
}
