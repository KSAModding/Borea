using System.Diagnostics.CodeAnalysis;
using Borea.Core.Mods;
using Borea.Core.Sharing;

namespace Borea.Core.Preview;

/// <summary>
/// Finds and reads what a 3D preview of a shared vehicle needs: the vehicle file, the parts of the game and the parts
/// of the mods the vehicle needs. It installs nothing, and it writes only into the preview cache of Borea.
/// </summary>
/// <remarks>
/// <see cref="PrepareAsync"/> never uses the network, so the vehicle page can call it as soon as it opens.
/// <see cref="LoadAsync"/> fetches the vehicle file or the missing mods only when the caller allows it, which is on a
/// click of the player, or when the page opens for a small vehicle file. Both calls return to the caller at once and do their file reads and the part catalog
/// build on the thread pool, so the UI thread can call them. A call after <see cref="IDisposable.Dispose"/> throws <see cref="ObjectDisposedException"/>,
/// and the dispose also disposes every <see cref="VehiclePreviewResult"/> that is still open.
/// </remarks>
public interface IVehiclePreview : IDisposable
{
    /// <summary>
    /// Finds out from the local files only whether a preview can be built now, and what it would have to fetch.
    /// </summary>
    /// <param name="release">The vehicle release the page shows.</param>
    /// <param name="targetInstanceId">The instance the player would install into, normally the active one, or null when there is none.</param>
    Task<VehiclePreviewPlan> PrepareAsync(ModVersionMetadata release, Guid? targetInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the vehicle and resolves its parts. A part that no local mod gives becomes a placeholder.
    /// </summary>
    /// <param name="fetchVehicle">True downloads the vehicle archive into the preview cache when no local copy is there.</param>
    /// <param name="fetchMods">True downloads the missing mods into the preview cache first, so that their parts resolve.</param>
    /// <returns>A result that the caller disposes, because it holds the archives open.</returns>
    Task<VehiclePreviewResult> LoadAsync(
        VehiclePreviewPlan plan,
        bool fetchVehicle,
        bool fetchMods,
        IProgress<VehiclePreviewStage>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the vehicle archive of the release into the preview cache from <paramref name="host"/>, which keeps no
    /// download count, with one plain request to the URL of the release, and does nothing when the cache has it.
    /// </summary>
    /// <exception cref="DownloadFailedException">The host did not serve the archive of the release.</exception>
    Task FetchFromShareHostAsync(ModVersionMetadata release, ShareHost host, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up the preview cache once on a background task: old archives and stills over the size limits and files of fetches
    /// that stopped. Only the App calls it, after its main window opens. A second call gives the same task.
    /// </summary>
    Task StartMaintenance();
}

/// <summary>What <see cref="IVehiclePreview.PrepareAsync"/> found.</summary>
public enum VehiclePreviewPlanState
{
    /// <summary>No game folder is set, so Borea has no parts to draw with.</summary>
    NoGameFolder,

    /// <summary>The game folder holds no Content/Core, so it is not a KSA install.</summary>
    GameFolderInvalid,

    /// <summary>The vehicle file is on disk, so a load uses no network. Missing mods still show as placeholders.</summary>
    CanBuildLocally,

    /// <summary>The vehicle file is not on disk. A load with fetchVehicle downloads <see cref="VehiclePreviewPlan.VehicleSizeBytes"/>.</summary>
    NeedsVehicleFile,

    /// <summary>The vehicle file on disk is not one the game can read as this release.</summary>
    VehicleDamaged,

    /// <summary>Something else stops the preview, see <see cref="VehiclePreviewPlan.Message"/>.</summary>
    Failed,
}

/// <param name="Release">The vehicle release the plan is for.</param>
/// <param name="TargetInstanceId">The instance the plan was made against.</param>
/// <param name="VehicleSizeBytes">The download size of the vehicle archive that the release states.</param>
/// <param name="MissingMods">The needed mods that are not on disk. The part counts are unknown until a load.</param>
/// <param name="GameBuild">The game build of the target instance, or null when Borea cannot read it.</param>
/// <param name="Message">Why the state is not a good one, for the player, or null.</param>
public sealed record VehiclePreviewPlan(
    VehiclePreviewPlanState State,
    ModVersionMetadata Release,
    Guid? TargetInstanceId,
    long? VehicleSizeBytes,
    IReadOnlyList<VehiclePreviewMissingMod> MissingMods,
    string? GameBuild,
    string? Message = null);

/// <summary>
/// A mod the vehicle needs that is not on disk. <paramref name="PartCount"/> is the number of placeholders that the
/// mod would fill, known only after a load and only when this is the one missing mod. <paramref name="CanFetch"/> is
/// true when the offline index has a release with a hash, which is what a fetch into the preview cache needs.
/// </summary>
public sealed record VehiclePreviewMissingMod(string ModId, ModVersion? Version, int? PartCount, long? SizeBytes, bool CanFetch);

/// <summary>What a load is doing now, for the progress text of the vehicle page.</summary>
public enum VehiclePreviewStage
{
    FetchingVehicle,
    ReadingParts,
    FetchingMods,
    Done,
}

/// <summary>What <see cref="IVehiclePreview.LoadAsync"/> gave.</summary>
public enum VehiclePreviewLoadState
{
    /// <summary>The vehicle resolved. <see cref="VehiclePreviewResult.Vehicle"/> and <see cref="VehiclePreviewResult.Assets"/> are set.</summary>
    Ready,

    NoGameFolder,

    GameFolderInvalid,

    /// <summary>The vehicle file is not on disk and the load was not allowed to fetch it.</summary>
    NeedsVehicleFile,

    /// <summary>The vehicle archive or its vehicle.xml cannot be read as this release.</summary>
    VehicleDamaged,

    /// <summary>Something else stops the preview, for example a failed download, see <see cref="VehiclePreviewResult.Message"/>.</summary>
    Failed,
}

/// <summary>
/// The resolved vehicle and the files to decode its meshes and textures from. Dispose closes the archives and lets the
/// preview cache evict them again. The caller disposes it when the page closes or shows another vehicle.
/// </summary>
public sealed class VehiclePreviewResult : IDisposable
{
    /// <param name="sceneKey">A hash of the game build, the mod sources in load order and the format version.</param>
    public VehiclePreviewResult(
        VehiclePreviewLoadState state,
        ResolvedVehicle? vehicle,
        IPreviewAssetSource? assets,
        IReadOnlyList<VehiclePreviewMissingMod> missingMods,
        IReadOnlyList<PreviewNotice> notices,
        string? sceneKey,
        string? vehicleSha256,
        string? gameBuild,
        string? message = null)
    {
        if (state == VehiclePreviewLoadState.Ready && (vehicle is null || assets is null))
            throw new ArgumentException("A ready result needs the vehicle and its assets.", nameof(state));

        State = state;
        Vehicle = vehicle;
        Assets = assets;
        MissingMods = missingMods ?? throw new ArgumentNullException(nameof(missingMods));
        Notices = notices ?? throw new ArgumentNullException(nameof(notices));
        SceneKey = sceneKey;
        VehicleSha256 = vehicleSha256;
        GameBuild = gameBuild;
        Message = message;
    }

    public VehiclePreviewLoadState State { get; }

    [MemberNotNullWhen(true, nameof(Vehicle), nameof(Assets))]
    public bool IsReady => State == VehiclePreviewLoadState.Ready;

    /// <summary>The scene. Its notices are the same list as <see cref="Notices"/>.</summary>
    public ResolvedVehicle? Vehicle { get; }

    /// <summary>The files the scene refers to. This result owns it, so the caller does not dispose it alone.</summary>
    public IPreviewAssetSource? Assets { get; }

    public IReadOnlyList<VehiclePreviewMissingMod> MissingMods { get; }

    /// <summary>Every notice of the load: the vehicle file, the mod sources, the part catalog, the mesh index and the resolver.</summary>
    public IReadOnlyList<PreviewNotice> Notices { get; }

    public string? SceneKey { get; }

    /// <summary>The lowercase SHA-256 of the vehicle archive that the release names.</summary>
    public string? VehicleSha256 { get; }

    public string? GameBuild { get; }

    public string? Message { get; }

    public void Dispose() => Assets?.Dispose();
}
