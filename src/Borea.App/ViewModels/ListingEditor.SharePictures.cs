using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.Core.Index;
using Borea.Core.Sharing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The pictures of a share, which the player chooses on the Describe step and which go up after the publish, because the host needs the listing first.
/// The release is live by then, so a picture that fails only waits for another try. The image card of the full form records an address that an author hosts, and here the host keeps the file, so only the picker and the reading of the bytes are shared with it.
/// </summary>
public sealed partial class ListingEditor
{
    /// <summary>The hosts that answered a picture with a 404, for as long as Borea runs, because the player chooses the pictures before the host.</summary>
    private readonly HashSet<string> _hostsWithoutPictures = new(StringComparer.Ordinal);

    /// <summary>The published listing that the pictures go to, which stays for another try.</summary>
    private (ShareHost Host, string ListingId)? _sharePictureTarget;

    private CancellationTokenSource? _sharePictureRun;

    private bool _hostTookNoPictures;

    public ObservableCollection<SharePictureRow> SharePictures { get; } = [];

    [ObservableProperty]
    private string? _sharePictureError;

    /// <summary>A host that the share can use may still take pictures, and only the answer to the first picture tells.</summary>
    public bool ShowsSharePictures => !IsAskingShareListing && (ShareHostRows.Count == 0 || ShareHostRows.Any(row => !_hostsWithoutPictures.Contains(row.Host.Key)));

    public bool CanAddSharePicture => SharePictures.Count < ShareImageLimits.MaxCount;

    public string SharePicturesHint => IsNewVersionShare
        ? Localization.FormatSharePicturesNewVersionHint(ShareImageLimits.MaxCount, ShareHostSizeText(ShareImageLimits.MaxBytes))
        : Localization.FormatSharePicturesHint(ShareImageLimits.MaxCount, ShareHostSizeText(ShareImageLimits.MaxBytes));

    public string? ShareSummaryPicturesText => SharePictures.Count > 0 ? Localization.FormatShareSummaryPictures(SharePictures.Count) : null;

    public bool IsSendingSharePictures => IsSharing && _shareHostRun == _sharePictureRun;

    /// <summary>Start over would drop what is on its way without a word. Cancel stands beside a picture, and it keeps the next try.</summary>
    public bool IsSendingShare => IsSendingSharePublish || IsSendingSharePictures;

    public bool CanRetrySharePictures => _sharePictureTarget is not null && !IsSharing && SharePictures.Any(row => !row.IsSent);

    /// <summary>How the pictures ended, which says each time that the release stays, because a player can read a failed picture as a failed share.</summary>
    public string? SharePicturesResultText
    {
        get
        {
            if (_sharePictureTarget is not { } target || IsSharing)
                return null;

            if (_hostTookNoPictures)
                return Localization.FormatSharePicturesNotOffered(target.Host.Name);

            var missing = SharePictures.Count(row => !row.IsSent);
            return SharePictures.Count == 0 ? null
                : missing > 0 ? Localization.FormatSharePicturesMissing(missing, SharePictures.Count)
                : Localization.SharePicturesSent;
        }
    }

    /// <summary>Takes one file at a time, with the same picker and the same reading of the bytes as the images of a listing, under the limits of the host.</summary>
    [RelayCommand]
    private async Task AddSharePictureAsync()
    {
        if (_share is not { } share || _owner.WindowServices is not { } window || !CanAddSharePicture)
            return;

        SharePictureError = null;
        PickedBinaryFile? file;
        _isPickingSharePicture = true;
        try
        {
            file = await window.OpenImageFileAsync(Localization.ListingChooseFile, Localization.ListingImageFileType, ShareImageLimits.MaxBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SharePictureError = Localization.SharePictureUnreadable;
            return;
        }
        finally
        {
            _isPickingSharePicture = false;
        }

        if (file is null || _share != share || !CanAddSharePicture)
            return;

        SharePictureError = SharePictureProblem(file);
        if (SharePictureError is not null)
            return;

        SharePictures.Add(new SharePictureRow(this, file.Name, file.Bytes));
        RefreshSharePictures();
    }

    private string? SharePictureProblem(PickedBinaryFile file)
    {
        if (file.Bytes.Length > ShareImageLimits.MaxBytes)
            return Localization.FormatSharePictureTooLarge(file.Name, ShareHostSizeText(ShareImageLimits.MaxBytes));

        return ContentImageBytes.Inspect(file.Bytes) switch
        {
            null => Localization.FormatSharePictureNotImage(file.Name),
            { Animated: true } => Localization.FormatSharePictureAnimated(file.Name),
            _ => null,
        };
    }

    internal void RemoveSharePicture(SharePictureRow row)
    {
        SharePictures.Remove(row);
        RefreshSharePictures();
    }

    [RelayCommand]
    private async Task RetrySharePicturesAsync()
    {
        if (CanRetrySharePictures)
            await SendSharePicturesAsync();
    }

    /// <summary>
    /// Sends one picture at a time in the order of the list, because the first that the host takes is the cover and only its website can change that.
    /// A picture that the host refuses leaves the others to go on. Any other failure stops the run, so a later picture does not take the place of the first.
    /// </summary>
    private async Task SendSharePicturesAsync()
    {
        if (_sharePictureTarget is not { } target || _owner.Services is not { } services)
            return;

        var rows = SharePictures.Where(row => !row.IsSent).ToList();
        if (rows.Count == 0)
            return;

        if (_hostsWithoutPictures.Contains(target.Host.Key))
        {
            TakeNoPictures(target.Host);
            return;
        }

        using var cancel = new CancellationTokenSource();
        _shareHostRun = cancel;
        _sharePictureRun = cancel;
        var client = services.ShareHostClient;
        var type = Type;
        try
        {
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                row.Error = null;
                try
                {
                    await SendSignedInAsync(target.Host, client, cancel, Localization.FormatSharePicturesSending(index + 1, rows.Count), async () =>
                    {
                        await client.UploadImageAsync(target.Host, type, target.ListingId, row.Bytes, cancel.Token);
                        return true;
                    });
                    row.MarkSent();
                }
                catch (ShareHostException exception)
                {
                    if (_shareHostRun == cancel && !TakeSharePictureFailure(target.Host, row, exception))
                        return;
                }
                catch (OperationCanceledException) when (_signInRun != cancel)
                {
                    // The bytes can have reached the host before the cancel.
                    row.Error = Localization.FormatSharePictureLostAnswer(target.Host.Name);
                    throw;
                }

                if (_shareHostRun != cancel)
                    return;
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        finally
        {
            if (_shareHostRun == cancel)
            {
                _shareHostRun = null;
                ShareHostProgress = null;
            }

            RefreshSharePictures();
        }
    }

    /// <summary>Says whether the next picture can go. A 404 with not_owner comes from a host that has the request, and any other 404 from a host that does not have it.</summary>
    private bool TakeSharePictureFailure(ShareHost host, SharePictureRow row, ShareHostException exception)
    {
        if (exception.IsNotFound && exception.Error != "not_owner")
        {
            TakeNoPictures(host);
            return false;
        }

        row.Error = SharePictureFailureText(host, exception);
        return exception.Error is "invalid_request" or "too_large";
    }

    /// <summary>
    /// A request with no clear answer can have reached the host, and only the website of the host can remove a picture that a next try adds twice, so the player looks first.
    /// The Done step has no sign-out, and a host also answers not_owner for a listing that it does not find, so that code has its own text here.
    /// </summary>
    private string SharePictureFailureText(ShareHost host, ShareHostException exception)
    {
        if (exception is { Failure: ShareHostFailure.NetworkError or ShareHostFailure.Unavailable } or { IsServerError: true })
        {
            var lost = Localization.FormatSharePictureLostAnswer(host.Name);
            return exception.IsServerError ? $"{lost}\n{exception.Message}" : lost;
        }

        if (exception.Error == "too_large")
            return Localization.FormatSharePictureTooLargeOnHost(host.Name);

        if (exception.Error == "not_owner")
            return Localization.FormatSharePictureNotOwner(host.Name, ShareListingName);

        var (text, detail, _) = ShareHostFailureText(host, exception, version: null);
        return detail is null ? text : $"{text}\n{detail}";
    }

    /// <summary>From the first moment of the Done step the pictures count as on their way, so nothing on that step drops them while the result is recorded.</summary>
    private void HoldSharePictures(ShareHost host, string listingId, CancellationTokenSource publish)
    {
        _sharePictureTarget = (host, listingId);
        _sharePictureRun = SharePictures.Count > 0 ? publish : null;
        RefreshSharePictures();
    }

    private void TakeNoPictures(ShareHost host)
    {
        _hostsWithoutPictures.Add(host.Key);
        _hostTookNoPictures = true;
        SharePictures.Clear();
    }

    private void ForgetSharePictures()
    {
        SharePictures.Clear();
        SharePictureError = null;
        _sharePictureTarget = null;
        _hostTookNoPictures = false;
        ForgetShareCover();
    }

    private void RefreshSharePictures()
    {
        for (var index = 0; index < SharePictures.Count; index++)
            SharePictures[index].IsCover = index == 0 && !IsNewVersionShare;

        OnPropertyChanged(nameof(IsSendingSharePictures));
        OnPropertyChanged(nameof(IsSendingShare));
        OnPropertyChanged(nameof(ShowsSharePictures));
        OnPropertyChanged(nameof(CanAddSharePicture));
        OnPropertyChanged(nameof(SharePicturesHint));
        OnPropertyChanged(nameof(ShareSummaryPicturesText));
        OnPropertyChanged(nameof(CanRetrySharePictures));
        OnPropertyChanged(nameof(SharePicturesResultText));
        RefreshShareCover();
    }
}
