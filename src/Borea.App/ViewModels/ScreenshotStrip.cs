using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The newest screenshots of an instance on its Game data tab, with the way to all of them in the Library.</summary>
public sealed partial class ScreenshotStrip(MainViewModel owner, Guid instanceId, IReadOnlyList<ScreenshotItem> items)
{
    public IReadOnlyList<ScreenshotItem> Items => items;

    [RelayCommand]
    private Task ShowAllAsync() => owner.ShowScreenshotsAsync(new ScreenshotSource(instanceId));
}
