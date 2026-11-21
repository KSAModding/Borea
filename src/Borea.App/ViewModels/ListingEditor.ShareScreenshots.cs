using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Screenshots;

namespace Borea.App.ViewModels;

/// <summary>
/// The newest screenshots of the instance that a vehicle or save comes from, which the Describe step offers under the button that picks a file.
/// A screenshot that the player chooses goes through the same checks as a picked file, so one above the limit of a host goes as a smaller copy.
/// </summary>
public sealed partial class ListingEditor
{
    private IReadOnlyList<ShareScreenshotChoice> _shareScreenshots = [];

    /// <summary>Completes when the screenshots of the last share are read, so a test can wait for it.</summary>
    internal Task ShareScreenshotsLoad { get; private set; } = Task.CompletedTask;

    /// <summary>Null while none can be chosen, so a share without screenshots builds no tiles.</summary>
    public IReadOnlyList<ShareScreenshotChoice>? ShownShareScreenshots => CanAddSharePicture && _shareScreenshots.Count > 0 ? _shareScreenshots : null;

    private void LoadShareScreenshots(GameSaveShareStart share) => ShareScreenshotsLoad = LoadShareScreenshotsAsync(share);

    private async Task LoadShareScreenshotsAsync(GameSaveShareStart share)
    {
        if (_owner.Services is not { } services)
            return;

        var screenshots = await _owner.ListScreenshotsAsync(services.Paths.GetInstanceScreenshotsFolder(share.InstanceId));
        if (_share != share)
            return;

        _shareScreenshots = screenshots.Take(MainViewModel.RecentScreenshotCount).Select(screenshot => new ShareScreenshotChoice(this, screenshot)).ToList();
        OnPropertyChanged(nameof(ShownShareScreenshots));
    }

    private void ForgetShareScreenshots()
    {
        _shareScreenshots = [];
        OnPropertyChanged(nameof(ShownShareScreenshots));
    }

    /// <summary>Reads at most one byte more than Borea makes fit, as the picker does, so a larger file shows as too large.</summary>
    internal async Task AddShareScreenshotAsync(Screenshot screenshot)
    {
        if (_share is not { } share || !CanAddSharePicture)
            return;

        SharePictureError = null;
        byte[] bytes;
        try
        {
            await using var file = new FileStream(screenshot.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
            bytes = new byte[Math.Min(file.Length, SharePictureFit.MaxSourceBytes + 1)];
            await file.ReadExactlyAsync(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SharePictureError = Localization.SharePictureUnreadable;
            return;
        }

        if (_share == share && CanAddSharePicture)
            await TakeSharePictureAsync(share, new PickedBinaryFile(Path.GetFileName(screenshot.Path), bytes));
    }
}
