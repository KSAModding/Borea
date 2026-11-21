using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>One choice of the filter of the Library screenshots: every screenshot, or those of one instance or of the game profile.</summary>
public sealed class ScreenshotFilter : ObservableObject
{
    private readonly Func<string> _name;

    /// <param name="source">The source whose screenshots the choice shows, or null for every screenshot.</param>
    internal ScreenshotFilter(Func<string> name, ScreenshotSource? source)
    {
        _name = name;
        Source = source;
    }

    public string Name => _name();

    internal ScreenshotSource? Source { get; }

    internal bool Shows(ScreenshotItem item) => Source is not { } source || item.Source == source;

    internal void RefreshText() => OnPropertyChanged(nameof(Name));
}
