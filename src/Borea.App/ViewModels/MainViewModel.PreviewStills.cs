using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Mods;
using Borea.Core.Preview;
using Borea.Preview.Loading;

namespace Borea.App.ViewModels;

/// <summary>The stills that Discover draws by itself for the vehicle rows without an icon.</summary>
public partial class MainViewModel
{
    /// <summary>The draws that ended since something last changed, so that a row that scrolls back asks the disk and a host no second time.</summary>
    private readonly HashSet<string> _stillAttempts = new(StringComparer.Ordinal);

    private PreviewStillQueue? _previewStills;

    internal PreviewStillQueue PreviewStills => _previewStills ??= new PreviewStillQueue(DrawStillAsync);

    /// <summary>
    /// Lets every row near the window ask again, after a change that can give it another still. A mod can arrive in
    /// any instance, the store or the cache of archives, and only the scene key of a new load knows about all of them, so
    /// this forgets every draw. A version is still fetched once, because the fetch leaves the file in the cache of
    /// previews and a fetch that fails makes the release wait for a click.
    /// </summary>
    internal void RefreshPreviewStills()
    {
        _stillAttempts.Clear();
        PreviewStills.Refresh();
    }

    /// <summary>
    /// Draws the still of a row that has none, or that has one with a note while its mods can have arrived. A vehicle
    /// made only of parts that are not here keeps its icon, as does a vehicle file that may not be fetched. Only a
    /// Discover page that shows draws, and its rows ask again when it comes back.
    /// </summary>
    private async Task DrawStillAsync(DiscoverItem row, CancellationToken token)
    {
        if (_services is not { } services || !CurrentWindowDiscover || IsChangingLibraryFolder || VehiclePreviewEncoder is null
            || row is { PreviewStillPath: not null, PreviewStillMissingMods: false }
            || row.LatestInChannel is not { Download.Sha256: { } } release)
            return;

        var target = ActiveInstance;
        var attempt = string.Join('|', release.Download.Sha256, PreviewStillBuild(services), TargetKey(target), LoadImagesFromAuthorHosts);
        if (!_stillAttempts.Add(attempt))
            return;

        try
        {
            var preview = VehiclePreviewServiceOf(services);
            var plan = await preview.PrepareAsync(release, target?.InstanceId, token);
            if (plan.State == VehiclePreviewPlanState.NeedsVehicleFile && await FetchStillVehicleAsync(services, preview, release, token))
                plan = await preview.PrepareAsync(release, target?.InstanceId, token);

            if (plan.State == VehiclePreviewPlanState.CanBuildLocally)
                await DrawStillFromPlanAsync(services, preview, plan, token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            _stillAttempts.Remove(attempt);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Without a still the row keeps its icon.
            services.Log.Write($"Could not draw the still of {release.ModId} {release.Version}.", exception);
        }
    }

    /// <summary>
    /// Fetches the vehicle file only under the setting, up to the size of a page that opens, and only from a share host
    /// that keeps no download count, so that scrolling past a vehicle raises no public number. A fetch that fails makes
    /// the page of the release wait for a click too, as a failed fetch of the page does.
    /// </summary>
    private async Task<bool> FetchStillVehicleAsync(BoreaServices services, IVehiclePreview preview, ModVersionMetadata release, CancellationToken token)
    {
        if (!LoadImagesFromAuthorHosts
            || release.Download is not { SizeBytes: <= VehiclePreviewViewModel.AutoFetchVehicleBytes, UnavailableSince: null }
            || !Uri.TryCreate(release.Download.Url, UriKind.Absolute, out var url)
            || _vehiclePreviewDeclined.Concat(_contentInstallReleases).Any(other => SameRelease(other, release)))
            return false;

        var snapshot = await services.CachedIndexSnapshots.GetSnapshotAsync(token);
        if (snapshot.ShareHosts.FirstOrDefault(host => host.KeepsNoDownloadCount && host.ServesDownload(url)) is not { } shareHost)
            return false;

        try
        {
            await preview.FetchFromShareHostAsync(release, shareHost, token);
            return true;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException or ObjectDisposedException))
        {
            services.Log.Write($"Could not fetch {release.ModId} {release.Version} for its still.", exception);
            _vehiclePreviewDeclined.Add(release);
            return false;
        }
    }

    private async Task DrawStillFromPlanAsync(BoreaServices services, IVehiclePreview preview, VehiclePreviewPlan plan, CancellationToken token)
    {
        using var result = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false, null, token);
        if (!result.IsReady || result is not { VehicleSha256: { } sha256, GameBuild: { } gameBuild, SceneKey: { } sceneKey })
            return;

        var missing = result.MissingMods.Count > 0 || result.Vehicle.Placeholders.Any(box => box.Reason is not (PlaceholderReason.AssetFailed or PlaceholderReason.Budget));
        if (!missing && result.Vehicle.Placeholders.Count > 0)
            return;

        // A new mod gives a new scene key, so a still with a note is drawn again and a still that fits stays.
        var key = missing ? sceneKey + VehiclePreviewViewModel.MissingModsKeySuffix : sceneKey;
        var thumbnails = services.PreviewThumbnails;
        if (!thumbnails.NeedsUpdate(sha256, gameBuild, key) || VehiclePreviewEncoder is not { } encoder)
            return;

        var builder = VehiclePreviewBuilderOf();
        try
        {
            var options = new PreviewLoadOptions();
            var (geometry, _) = await builder.BuildGeometryAsync(result.Vehicle, result.Assets, options, token);
            var (scene, notices) = await builder.AddTexturesAsync(geometry, result.Vehicle, result.Assets, options, token);
            if (scene.Draws.Count == 0 || (!missing && scene.Placeholders.Count > 0))
                return;

            var partial = !missing && notices.Any(notice => notice.Kind == PreviewNoticeKind.AssetFailed);
            if (await Task.Run(() => VehiclePreviewViewModel.TryStoreStill(thumbnails, encoder, scene, sha256, gameBuild, key, partial, token), token))
                ShowPreviewStill(sha256);
        }
        finally
        {
            builder.MeshCache.Clear();
        }
    }

    /// <summary>Gives a row the still that is stored for its latest release, with the note of a still that was drawn while mods were missing. A listing icon always wins.</summary>
    private static void ShowStoredStill(DiscoverItem item, IPreviewThumbnails thumbnails, string? build)
    {
        var sha256 = item.LatestInChannel?.Download.Sha256;
        var path = item.HasDrawnStill ? thumbnails.TryGetPath(sha256, build) : null;
        item.PreviewStillMissingMods = path is not null && thumbnails.TryGetSceneKey(sha256, build)?.EndsWith(VehiclePreviewViewModel.MissingModsKeySuffix, StringComparison.Ordinal) == true;
        item.PreviewStillPath = path;
    }
}
