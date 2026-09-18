using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The Home banner for a newer public game build, and the patch notes the installed game ships.</summary>
public partial class MainViewModel
{
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

    /// <summary>The builds in Content/Versions of the game folder, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoGamePatchNotes))]
    private IReadOnlyList<GamePatchNotesItem> _gamePatchNotes = [];

    public bool HasNoGamePatchNotes => GamePatchNotes.Count == 0;

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
        GamePatchNotes = notes.Select(entry => new GamePatchNotesItem(entry)).ToList();
        GamePatchNotesError = null;
        IsGamePatchNotesOpen = true;
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

/// <summary>The patch notes of one installed game build.</summary>
public sealed class GamePatchNotesItem : ObservableObject
{
    private readonly DateOnly? _date;

    public string Build { get; }

    public IReadOnlyList<string> Lines { get; }

    public string? DateText => _date?.ToString("d", CultureInfo.CurrentCulture);

    public GamePatchNotesItem(GamePatchNotes notes)
    {
        Build = notes.Build;
        Lines = notes.Lines;
        _date = notes.Date;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(DateText));
}
