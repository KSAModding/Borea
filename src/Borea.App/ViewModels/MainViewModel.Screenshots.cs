using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Screenshots;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The screenshots that the game saves in each instance and in the game profile. Each of them has its own folder, so the Screenshots tab of
/// the Library is the one place that shows them all, newest first. The tab reads the folders each time it opens and forgets them when it closes,
/// so the tiles give their pictures back.
/// </summary>
public partial class MainViewModel
{
    private IReadOnlyList<ScreenshotItem> _screenshots = [];
    private Task _screenshotLoad = Task.CompletedTask;
    private int _screenshotLoadGeneration;

    /// <summary>The source that the chosen filter shows, or null for every screenshot, which the next load chooses again.</summary>
    private ScreenshotSource? _shownScreenshotSource;

    public ObservableCollection<ScreenshotFilter> ScreenshotFilters { get; } = [];

    [ObservableProperty]
    private ScreenshotFilter? _selectedScreenshotFilter;

    /// <summary>A new list for each filter, because thousands of single changes cost the grid more than one new layout.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenshotCountText))]
    private IReadOnlyList<ScreenshotItem> _shownScreenshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshots))]
    [NotifyPropertyChangedFor(nameof(ShowsScreenshotsEmpty))]
    private bool _isLoadingScreenshots;

    public bool HasScreenshots => !IsLoadingScreenshots && _screenshots.Count > 0;

    public bool ShowsScreenshotsEmpty => !IsLoadingScreenshots && _screenshots.Count == 0;

    public string ScreenshotCountText => Localization.FormatScreenshotCount(ShownScreenshots.Count);

    /// <summary>Completes when the last load of the Screenshots tab is done.</summary>
    internal Task WhenScreenshotsLoadedAsync() => _screenshotLoad;

    [RelayCommand]
    private Task ShowLibraryScreenshotsAsync() => ShowScreenshotsAsync(source: null);

    [RelayCommand]
    private Task ReloadScreenshotsAsync() => _screenshotLoad = LoadScreenshotsAsync();

    /// <summary>Opens the Screenshots tab of the Library with the filter on <paramref name="source"/>, or on every screenshot when it is null.</summary>
    internal Task ShowScreenshotsAsync(ScreenshotSource? source)
    {
        _shownScreenshotSource = source;
        SetMainWindowLibrary();
        LibraryTab = LibraryTab.Screenshots;
        return ReloadScreenshotsAsync();
    }

    partial void OnLibraryTabChanged(LibraryTab value)
    {
        if (value != LibraryTab.Screenshots)
            ForgetScreenshots();
    }

    partial void OnSelectedScreenshotFilterChanged(ScreenshotFilter? value)
    {
        // the list of choices is filled anew on each load, and the box clears its choice on the way
        if (value is null)
            return;

        _shownScreenshotSource = value.Source;
        ShownScreenshots = _screenshots.Where(value.Shows).ToList();
    }

    private async Task LoadScreenshotsAsync()
    {
        if (_services is not { } services)
            return;

        var generation = ++_screenshotLoadGeneration;
        IsLoadingScreenshots = true;
        var sources = Instances
            .Select(item => (Source: new ScreenshotSource(item.InstanceId), Name: Constant(item.Name), Folder: services.Paths.GetInstanceScreenshotsFolder(item.InstanceId)))
            .Append((Source: new ScreenshotSource(null), Name: () => Localization.SharedProfileInstanceName, Folder: services.Paths.GetSharedProfileScreenshotsFolder()))
            .ToList();
        var lists = await Task.WhenAll(sources.Select(source => ListScreenshotsAsync(source.Folder)));
        if (generation != _screenshotLoadGeneration)
            return;

        var filters = new List<ScreenshotFilter> { new(() => Localization.ScreenshotsAll, null) };
        var screenshots = new List<ScreenshotItem>();
        for (var index = 0; index < sources.Count; index++)
        {
            var (source, name, _) = sources[index];
            if (lists[index].Count == 0)
                continue;

            filters.Add(new ScreenshotFilter(name, source));
            screenshots.AddRange(lists[index].Select(screenshot => new ScreenshotItem(this, screenshot, source, name)));
        }

        SetScreenshots(screenshots
            .OrderByDescending(item => item.Screenshot.TakenAt)
            .ThenBy(item => Path.GetFileName(item.Screenshot.Path), StringComparer.Ordinal)
            .ToList());
        Arrange(ScreenshotFilters, filters);
        SelectedScreenshotFilter = filters.FirstOrDefault(filter => filter.Source == _shownScreenshotSource) ?? filters[0];
        IsLoadingScreenshots = false;

        static Func<string> Constant(string text) => () => text;
    }

    private void ForgetScreenshots()
    {
        _screenshotLoadGeneration++;
        SetScreenshots([]);
        ShownScreenshots = [];
        ScreenshotFilters.Clear();
        SelectedScreenshotFilter = null;
        IsLoadingScreenshots = false;
    }

    private void SetScreenshots(IReadOnlyList<ScreenshotItem> screenshots)
    {
        _screenshots = screenshots;
        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(ShowsScreenshotsEmpty));
    }

    /// <summary>The screenshots in <paramref name="folder"/>, newest first. A folder that cannot be read gives none and a line in the log.</summary>
    internal async Task<IReadOnlyList<Screenshot>> ListScreenshotsAsync(string folder)
    {
        if (_services is not { } services)
            return [];

        try
        {
            return await services.Screenshots.ListAsync(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            services.Log.Write($"Borea could not read the screenshots in {folder}. {exception.Message}");
            return [];
        }
    }

    internal void OpenScreenshot(string path) => ShowOpenError(() => PathName(path), TryOpenWithSystem(path));

    internal void ShowScreenshotInFolder(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? path;
        ShowOpenError(() => PathName(folder), TryOpenWithSystem(folder));
    }

    internal Task CopyScreenshotPathAsync(string path)
        => CopyTextAsync(() => Task.FromResult(path), () => PathName(path), () => Localization.ScreenshotPathCopied);

    private void RefreshScreenshotText()
    {
        foreach (var filter in ScreenshotFilters)
            filter.RefreshText();
        foreach (var item in _screenshots)
            item.RefreshText();
        OnPropertyChanged(nameof(ScreenshotCountText));
    }
}
