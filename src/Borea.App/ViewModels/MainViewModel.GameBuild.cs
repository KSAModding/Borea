using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The Home banner for a newer public game build, and the patch notes of the installed and the newer builds.</summary>
public partial class MainViewModel
{
    internal const int MaxNewerGamePatchNotes = 20;

    private CancellationTokenSource? _newerGamePatchNotesLoad;

    private Task? _newerGamePatchNotesTask;

    private Task? _gameBuildCheck;

    private BoreaServices? _gameBuildCheckServices;

    private int? _dismissedGameRevision;

    /// <summary>What the master server reported, or null before it answered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewerGameBuild))]
    [NotifyPropertyChangedFor(nameof(GameBuildBannerText))]
    [NotifyPropertyChangedFor(nameof(GameBuildDownloadUrl))]
    [NotifyPropertyChangedFor(nameof(ShowGameBuildBanner))]
    private LatestVersionInfo? _latestGameBuild;

    /// <summary>The public build when its revision is higher than the installed one (RFC 0017), else null.</summary>
    public GameVersion? NewerGameBuild =>
        LatestGameBuild?.Version is { } latest && GameVersion.TryParse(InstalledVersionText, out var installed) && latest > installed
            ? latest
            : null;

    public string? GameBuildBannerText => NewerGameBuild is { } build ? $"{Localization.GameBuildAvailable} {build}" : null;

    /// <summary>The download page the master server names, when it is a web address.</summary>
    public string? GameBuildDownloadUrl =>
        NewerGameBuild is not null
        && Uri.TryCreate(LatestGameBuild?.DownloadUrl, UriKind.Absolute, out var page)
        && (page.Scheme == Uri.UriSchemeHttps || page.Scheme == Uri.UriSchemeHttp)
            ? page.AbsoluteUri
            : null;

    public bool ShowGameBuildBanner => NewerGameBuild is { } build
        && build.Revision > (_dismissedGameRevision ?? _appPreferences.DismissedGameRevision ?? -1);

    [ObservableProperty]
    private bool _isGamePatchNotesOpen;

    [ObservableProperty]
    private string? _gamePatchNotesError;

    /// <summary>The newer builds that are not installed, then the builds in Content/Versions of the game folder, each newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoGamePatchNotes))]
    private IReadOnlyList<GamePatchNotesItem> _gamePatchNotes = [];

    public bool HasNoGamePatchNotes => GamePatchNotes.Count == 0;

    [ObservableProperty]
    private bool _isLoadingNewerGamePatchNotes;

    [ObservableProperty]
    private bool _newerGamePatchNotesFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewerGamePatchNotesCappedText))]
    private bool _newerGamePatchNotesCapped;

    public string? NewerGamePatchNotesCappedText => NewerGamePatchNotesCapped ? Localization.FormatGamePatchNotesCapped(MaxNewerGamePatchNotes) : null;

    partial void OnInstalledVersionTextChanged(string? value)
    {
        OnPropertyChanged(nameof(NewerGameBuild));
        OnPropertyChanged(nameof(GameBuildBannerText));
        OnPropertyChanged(nameof(GameBuildDownloadUrl));
        OnPropertyChanged(nameof(ShowGameBuildBanner));
        StartGameBuildCheck();
    }

    /// <summary>Asks the master server once per service graph until it answered, and only when the installed build is known.</summary>
    private void StartGameBuildCheck()
    {
        if (LatestGameBuild is not null || _services is not { } services || ReferenceEquals(services, _gameBuildCheckServices)
            || !_appPreferences.CheckForUpdatesAtStart || !GameVersion.TryParse(InstalledVersionText, out _))
            return;

        _gameBuildCheckServices = services;
        _gameBuildCheck = CheckGameBuildAsync(services);
    }

    /// <summary>Completes when the master server check has finished.</summary>
    internal Task WhenGameBuildCheckedAsync() => _gameBuildCheck ?? Task.CompletedTask;

    private async Task CheckGameBuildAsync(BoreaServices services)
    {
        try
        {
            var answer = await services.LatestVersion.PingAsync();
            if (answer.Version is not null)
                LatestGameBuild = answer;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            // no answer shows no banner
        }
    }

    [RelayCommand]
    private async Task OpenGamePatchNotesAsync()
    {
        if (_services is not { } services)
            return;

        var notes = await services.GamePatchNotes.ReadAsync();
        CancelNewerGamePatchNotes();
        GamePatchNotes = notes.Select(entry => new GamePatchNotesItem(entry, isInstalled: true)).ToList();
        GamePatchNotesError = null;
        NewerGamePatchNotesFailed = false;
        NewerGamePatchNotesCapped = false;
        IsGamePatchNotesOpen = true;

        var load = new CancellationTokenSource();
        _newerGamePatchNotesLoad = load;
        _newerGamePatchNotesTask = LoadNewerGamePatchNotesAsync(services, load.Token);
    }

    /// <summary>Completes when the notes of the newer builds have loaded, failed or were cancelled.</summary>
    internal Task WhenNewerGamePatchNotesLoadedAsync() => _newerGamePatchNotesTask ?? Task.CompletedTask;

    private async Task LoadNewerGamePatchNotesAsync(BoreaServices services, CancellationToken cancellationToken)
    {
        try
        {
            if (!GameVersion.TryParse(InstalledVersionText, out var installed))
                return;

            var builds = (await GameReleasesAsync(services, cancellationToken)).NewerThan(installed.Revision);
            cancellationToken.ThrowIfCancellationRequested();
            if (builds.Count == 0)
                return;

            IsLoadingNewerGamePatchNotes = true;
            var fetch = await services.GamePatchNotesFetcher.FetchAsync(builds, installed.Revision, MaxNewerGamePatchNotes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var local = GamePatchNotes.Select(item => item.Revision).ToHashSet();
            GamePatchNotes =
            [
                .. fetch.Notes.Where(entry => !local.Contains(entry.Revision)).Select(entry => new GamePatchNotesItem(entry, isInstalled: false)),
                .. GamePatchNotes,
            ];
            NewerGamePatchNotesFailed = !fetch.Complete;
            NewerGamePatchNotesCapped = fetch.Capped;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            services.Log.Write("The patch notes of the newer game builds did not load.", exception);
            NewerGamePatchNotesFailed = true;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsLoadingNewerGamePatchNotes = false;
        }
    }

    /// <summary>game_versions of the index snapshot and the master server's build (RFC 0017). The snapshot is read from disk when Discover has not loaded it.</summary>
    private async Task<GameReleaseList> GameReleasesAsync(BoreaServices services, CancellationToken cancellationToken)
    {
        var releases = _gameReleases;
        if (releases.IsEmpty)
        {
            try
            {
                releases = GameReleaseList.From((await services.IndexReader.ReadAsync(cancellationToken)).GameVersions);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return LatestGameBuild?.Version is { } latest ? releases.WithBuild(latest) : releases;
    }

    private void CancelNewerGamePatchNotes()
    {
        if (_newerGamePatchNotesLoad is { } load)
        {
            load.Cancel();
            load.Dispose();
            _newerGamePatchNotesLoad = null;
        }

        IsLoadingNewerGamePatchNotes = false;
    }

    partial void OnIsGamePatchNotesOpenChanged(bool value)
    {
        if (!value)
            CancelNewerGamePatchNotes();
    }

    [RelayCommand]
    private void OpenGameBuildDownload()
    {
        if (GameBuildDownloadUrl is { } url)
            GamePatchNotesError = TryOpenWithSystem(url);
    }

    [RelayCommand]
    private void CloseGamePatchNotes() => IsGamePatchNotesOpen = false;

    [RelayCommand]
    private void DismissGameBuildBanner()
    {
        if (NewerGameBuild is not { } build)
            return;

        _dismissedGameRevision = build.Revision;
        OnPropertyChanged(nameof(ShowGameBuildBanner));
        QueuePreferenceSave(preferences => preferences.WithDismissedGameRevision(build.Revision));
    }
}

/// <summary>The patch notes of one game build.</summary>
public sealed class GamePatchNotesItem : ObservableObject
{
    private readonly DateOnly? _date;

    public string Build { get; }

    public int Revision { get; }

    public bool IsInstalled { get; }

    public IReadOnlyList<string> Lines { get; }

    public string? DateText => _date?.ToString("d", CultureInfo.CurrentCulture);

    public GamePatchNotesItem(GamePatchNotes notes, bool isInstalled)
    {
        Build = notes.Build;
        Revision = notes.Revision;
        IsInstalled = isInstalled;
        Lines = notes.Lines;
        _date = notes.Date;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(DateText));
}
