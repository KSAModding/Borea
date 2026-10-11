using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Listings;

namespace Borea.App.ViewModels;

/// <summary>
/// Reads the hosted images of the rows. A listed listing reads each image once when it opens, and a row whose bytes differ from its
/// record says what changed. An address on a branch gets the commit address of the same bytes as a suggestion.
/// </summary>
public sealed partial class ListingEditor
{
    private CancellationTokenSource? _imageWork;

    /// <summary>The last check of the images of a listed listing, which tests wait for.</summary>
    internal Task ImageCheck { get; private set; } = Task.CompletedTask;

    private IEnumerable<ListingImageRow> ImageRows => Icon is { } icon ? [icon, .. DescriptionImages] : DescriptionImages;

    private CancellationToken ImageToken => (_imageWork ??= new CancellationTokenSource()).Token;

    internal async Task MeasureImageAsync(ListingImageRow row)
    {
        if (_owner.Services is not { } services)
            return;

        var url = row.Url.Trim();
        var token = ImageToken;
        row.IsMeasuring = true;
        try
        {
            var measurement = await services.ListingImages.MeasureAsync(url, row.Role, token);
            if (token.IsCancellationRequested)
                return;

            row.Apply(measurement);
            if (measurement.Sha256 is { } sha256)
                await FindCommitAddressAsync(services, row, url, sha256, token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or HttpRequestException)
        {
        }
        finally
        {
            row.IsMeasuring = false;
        }
    }

    /// <summary>Marks every measured row of the listing that just opened, and reads their images.</summary>
    private void CheckListedImages()
    {
        foreach (var row in ImageRows)
            row.AwaitsCheck = row.IsMeasured && row.Url.Trim().Length > 0;
        ResumeImageCheck();
    }

    /// <summary>Reads the images that the check did not read yet, such as after the page was left while it ran.</summary>
    private void ResumeImageCheck()
    {
        if (!IsFormStep || _owner.Services is not { } services)
            return;

        var rows = ImageRows.Where(row => row.AwaitsCheck).ToList();
        if (rows.Count == 0)
            return;

        var token = ImageToken;
        ImageCheck = Task.WhenAll(rows.Select(row => CheckImageAsync(services, row, token)));
    }

    /// <summary>Stops every read of an image, because the editor leaves the listing or the page.</summary>
    private void StopImageCheck()
    {
        _imageWork?.Cancel();
        _imageWork = null;
    }

    /// <summary>Stops the reads and forgets the rows that still wait for one, because the editor drops the listing.</summary>
    private void DropImageCheck()
    {
        StopImageCheck();
        foreach (var row in ImageRows)
            row.AwaitsCheck = false;
    }

    private static async Task CheckImageAsync(BoreaServices services, ListingImageRow row, CancellationToken cancellationToken)
    {
        var url = row.Url.Trim();
        try
        {
            var check = await services.ListingImages.CheckAsync(url, row.Role, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            row.Checked(url, check.Measurement);
            row.FoundBranch(url, check.Branch);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or HttpRequestException)
        {
        }
    }

    private static async Task FindCommitAddressAsync(BoreaServices services, ListingImageRow row, string url, string sha256, CancellationToken cancellationToken)
    {
        if (GitHubBranchAddress.Of(url) is null)
            return;

        var branch = await services.ListingImages.FindCommitAddressAsync(url, row.Role, sha256, cancellationToken);
        if (!cancellationToken.IsCancellationRequested)
            row.FoundBranch(url, branch);
    }

    internal string ImageChangeText(ListingImageRow row, ListingImageMeasurement found)
    {
        var changes = new List<string>();
        if (row.Width != found.Width || row.Height != found.Height)
            changes.Add(Localization.FormatListingImageChangedPixels(Number(row.Width), Number(row.Height), Number(found.Width), Number(found.Height)));
        if (row.Size != found.Size)
            changes.Add(Localization.FormatListingImageChangedBytes(Bytes(row.Size), Bytes(found.Size)));
        if (changes.Count == 0)
            changes.Add(Localization.ListingImageChangedOnlySha);
        return Localization.FormatListingImageChanged(string.Join(", ", changes));
    }

    internal string ImageCheckProblemText(string problem) => Localization.FormatListingImageCheckFailed(problem.TrimEnd('.'));

    internal string ImageNoLongerFitsText(string problem) => Localization.FormatListingImageNoLongerFits(problem.TrimEnd('.'));

    internal string ImageBranchNote(string? branch) => branch is null ? Localization.ListingImageBranchUnnamed : Localization.FormatListingImageBranch(branch);

    internal string ImageCommitText(string url) => Localization.FormatListingImageCommitAddress(url);

    private static string Number(long? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "?";

    private static string Bytes(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "?";
}
