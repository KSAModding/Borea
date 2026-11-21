using System;
using System.Threading.Tasks;
using Borea.Core.Screenshots;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>Where a screenshot comes from: the instance of the id, or the game profile when the id is null.</summary>
internal readonly record struct ScreenshotSource(Guid? InstanceId);

/// <summary>One screenshot on a tile, with the instance it comes from and the time the game saved it.</summary>
public sealed partial class ScreenshotItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly Func<string> _sourceName;

    internal ScreenshotItem(MainViewModel owner, Screenshot screenshot, ScreenshotSource source, Func<string> sourceName)
    {
        _owner = owner;
        _sourceName = sourceName;
        Screenshot = screenshot;
        Source = source;
    }

    public Screenshot Screenshot { get; }

    internal ScreenshotSource Source { get; }

    public string SourceName => _sourceName();

    public string TakenText => MainViewModel.DateTimeText(new DateTimeOffset(Screenshot.TakenAt));

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(TakenText));
    }

    [RelayCommand]
    private void Open() => _owner.OpenScreenshot(Screenshot.Path);

    [RelayCommand]
    private void ShowInFolder() => _owner.ShowScreenshotInFolder(Screenshot.Path);

    [RelayCommand]
    private Task CopyPathAsync() => _owner.CopyScreenshotPathAsync(Screenshot.Path);
}
