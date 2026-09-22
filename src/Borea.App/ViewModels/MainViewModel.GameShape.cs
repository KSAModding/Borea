using System;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The Home banner for a game that does not have the shape Borea expects.</summary>
public partial class MainViewModel
{
    private const string GameShapeIssuesUrl = "https://github.com/KSAModding/Borea/issues";

    /// <summary>What the last check found, or null before it ran.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGameShapeBrokenBanner))]
    [NotifyPropertyChangedFor(nameof(ShowGameShapeUntestedBanner))]
    [NotifyPropertyChangedFor(nameof(GameShapeBrokenText))]
    [NotifyPropertyChangedFor(nameof(GameShapeUntestedText))]
    private GameShape? _gameShape;

    private int? _dismissedUntestedGameRevision;

    public bool ShowGameShapeBrokenBanner => GameShape?.Status == GameShapeStatus.Broken;

    /// <summary>
    /// Every build after the newest verified one is untested, which is the
    /// normal state between a game patch and the next Borea release, so the
    /// notice is shown once per build and not at every start.
    /// </summary>
    public bool ShowGameShapeUntestedBanner => GameShape is { Status: GameShapeStatus.Untested, Version.Version: { } build }
        && build.Revision != (_dismissedUntestedGameRevision ?? _appPreferences.DismissedUntestedGameRevision);

    public string? GameShapeBrokenText => GameShape is { Status: GameShapeStatus.Broken } shape
        ? Localization.FormatGameShapeBroken(shape.Version is null ? null : shape.BuildText, GameShapeText.Broken(shape.Broken))
        : null;

    public string? GameShapeUntestedText => GameShape is { Status: GameShapeStatus.Untested } shape
        ? Localization.FormatGameShapeUntested(shape.BuildText, shape.VerifiedBuilds.Newest.ToString())
        : null;

    /// <summary>
    /// Runs the check for the installation and the profile the game keeps
    /// itself, which is what Home is about. An instance has a shape of its own,
    /// and the page that works with it asks for that one. The install part is
    /// checked once per game build, so calling this again is cheap.
    /// </summary>
    internal async Task RefreshGameShapeAsync()
    {
        if (_services is not { } services)
            return;

        var shape = await services.GameShape.GetAsync();

        if (shape.Status == GameShapeStatus.Broken && GameShape?.Status != GameShapeStatus.Broken)
            services.Log.Write($"The game does not have the shape Borea expects. {shape.BrokenText}");

        GameShape = shape;
    }

    [RelayCommand]
    private void ReportGameShape() => ShowOpenError(() => GameShapeIssuesUrl, TryOpenWithSystem(GameShapeIssuesUrl));

    [RelayCommand]
    private void DismissGameShapeUntested()
    {
        if (GameShape?.Version?.Version is not { } build)
            return;

        _dismissedUntestedGameRevision = build.Revision;
        OnPropertyChanged(nameof(ShowGameShapeUntestedBanner));
        QueuePreferenceSave(preferences => preferences.WithDismissedUntestedGameRevision(build.Revision));
    }
}
