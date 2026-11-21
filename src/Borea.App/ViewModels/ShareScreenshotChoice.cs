using System;
using System.Threading.Tasks;
using Borea.Core.Screenshots;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>A screenshot of the instance that a share comes from, which the Describe step offers as a picture with one click.</summary>
public sealed partial class ShareScreenshotChoice
{
    private readonly ListingEditor _owner;

    internal ShareScreenshotChoice(ListingEditor owner, Screenshot screenshot)
    {
        _owner = owner;
        Screenshot = screenshot;
    }

    public Screenshot Screenshot { get; }

    public string TakenText => MainViewModel.DateTimeText(new DateTimeOffset(Screenshot.TakenAt));

    [RelayCommand]
    private Task AddAsync() => _owner.AddShareScreenshotAsync(Screenshot);
}
