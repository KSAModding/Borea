using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The cover that Borea draws for a new vehicle listing on the Publish step. It goes up as the first picture, and Publish waits for the drawing, so the player has seen the picture before it goes.
/// A picture of the player wins, and a new version sends none, because a listing that exists keeps its cover.
/// </summary>
public sealed partial class ListingEditor
{
    private CancellationTokenSource? _shareCoverRun;

    /// <summary>The newest file of the vehicle when the last drawing started.</summary>
    private DateTime? _shareCoverFilesTime;

    /// <summary>A drawing that ended without a picture, for which the step says that no cover comes.</summary>
    private bool _isShareCoverMissing;

    /// <summary>The file picker of the step is open, and the picture that it can still bring wins over the cover.</summary>
    private bool _isPickingSharePicture;

    /// <summary>The last drawing, which tests wait for.</summary>
    internal Task ShareCoverDraw { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private byte[]? _shareCoverBytes;

    [ObservableProperty]
    private bool _isDrawingShareCover;

    [ObservableProperty]
    private bool _isShareCoverLeftOut;

    private bool CanOfferShareCover => _share is { Entry.Kind: GameSaveKind.Vehicle } && !IsNewVersionShare && SharePictures.Count == 0 && ShowsSharePictures;

    public bool ShowsShareCoverDrawing => CanOfferShareCover && IsDrawingShareCover;

    public bool ShowsShareCoverPicture => CanOfferShareCover && ShareCoverBytes is not null && !IsShareCoverLeftOut;

    public bool ShowsShareCoverLeftOut => CanOfferShareCover && ShareCoverBytes is not null && IsShareCoverLeftOut;

    public bool ShowsShareCoverMissing => CanOfferShareCover && _isShareCoverMissing;

    public bool ShowsShareCoverCard => ShowsShareCoverDrawing || ShowsShareCoverPicture || ShowsShareCoverLeftOut || ShowsShareCoverMissing;

    /// <summary>The picture that the step shows now. The player can still leave it out or pick another while a sign-in holds the publish, so the publish reads this when it ends.</summary>
    private byte[]? ShareCoverToSend => ShowsShareCoverPicture && !_isPickingSharePicture ? ShareCoverBytes : null;

    [RelayCommand]
    private void LeaveOutShareCover() => IsShareCoverLeftOut = true;

    [RelayCommand]
    private void UseShareCover() => IsShareCoverLeftOut = false;

    /// <summary>Each visit of the step draws again, because the player can have changed the vehicle or removed a picture of their own.</summary>
    private void StartShareCover()
    {
        StopShareCover();
        if (_share is not { } share || !CanOfferShareCover)
            return;

        var run = new CancellationTokenSource();
        _shareCoverRun = run;
        _shareCoverFilesTime = ShareCoverFilesTime(share);
        IsDrawingShareCover = true;
        RefreshShareCover();
        ShareCoverDraw = DrawShareCoverAsync(share, run);
    }

    /// <summary>A check writes the archive from the vehicle as it is now, and the game can have changed the vehicle since the drawing, so the picture follows.</summary>
    private void RedrawShareCoverIfVehicleChanged()
    {
        if (_share is { } share && ShareCoverFilesTime(share) != _shareCoverFilesTime)
            StartShareCover();
    }

    /// <summary>Null when the folder cannot be read, which the drawing then finds out for itself.</summary>
    private static DateTime? ShareCoverFilesTime(GameSaveShareStart share)
    {
        try
        {
            return new DirectoryInfo(share.Entry.Path).EnumerateFiles("*", SearchOption.AllDirectories).Max(file => (DateTime?)file.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>An exception still faults the task, and the step goes on without a cover.</summary>
    private async Task DrawShareCoverAsync(GameSaveShareStart share, CancellationTokenSource run)
    {
        byte[]? bytes = null;
        try
        {
            bytes = await _owner.DrawShareCoverAsync(share, run.Token);
        }
        finally
        {
            if (_shareCoverRun == run)
            {
                _shareCoverRun = null;
                ShareCoverBytes = bytes;
                IsDrawingShareCover = false;
                _isShareCoverMissing = bytes is null;
                RefreshShareCover();
            }

            run.Dispose();
        }
    }

    private void StopShareCover()
    {
        _shareCoverRun?.Cancel();
        _shareCoverRun = null;
        ShareCoverBytes = null;
        IsDrawingShareCover = false;
        _isShareCoverMissing = false;
        RefreshShareCover();
    }

    private void ForgetShareCover()
    {
        StopShareCover();
        IsShareCoverLeftOut = false;
    }

    /// <summary>The row that the Done step lists and that a next try sends, as for a picture of the player.</summary>
    private void AddShareCoverRow()
    {
        if (ShareCoverToSend is { } cover)
            SharePictures.Add(new SharePictureRow(this, Localization.ShareCoverRowName, cover));
    }

    partial void OnIsShareCoverLeftOutChanged(bool value) => RefreshShareCover();

    private void RefreshShareCover()
    {
        OnPropertyChanged(nameof(ShowsShareCoverDrawing));
        OnPropertyChanged(nameof(ShowsShareCoverPicture));
        OnPropertyChanged(nameof(ShowsShareCoverLeftOut));
        OnPropertyChanged(nameof(ShowsShareCoverMissing));
        OnPropertyChanged(nameof(ShowsShareCoverCard));
        OnPropertyChanged(nameof(CanPublishToShareHost));
    }
}
