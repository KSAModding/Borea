using System.Security.Cryptography;
using System.Text;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Planning;
using Borea.Core.Preview;
using Borea.Core.Sharing;
using Borea.Core.State;
using Borea.Core.UserContent;
using Borea.Storage.Files;
using Borea.Storage.Mods;
using Borea.Storage.Preview.Ksa;
using Borea.Storage.Preview.Sources;

namespace Borea.Storage.Preview;

/// <summary>
/// The <see cref="IVehiclePreview"/> over the local files: <see cref="PreviewSources"/> finds the vehicle and its mods,
/// <see cref="PartCatalog"/> and <see cref="MeshIndex"/> read the parts of those mods, and
/// <see cref="VehicleSceneResolver"/> places them. A fetch goes only into the <see cref="PreviewArchiveCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// The parts come from the game of the target instance (<see cref="IInstanceGameResolver"/>), which is its kept build
/// when it keeps one. A kept build that is missing or reads another version stops the preview, so the build of the
/// settings never stands in for it.
/// </para>
/// <para>
/// The catalog and the mesh index of the last few mod sets stay in memory, keyed by the game build and the source of
/// each mod in load order, because reading the stock parts takes most of the time of a load. They hold no open file.
/// The archives that an open result reads stay pinned in the preview cache until the result is disposed.
/// </para>
/// <para>
/// The game names no mod for a part Id (SerializedId.Mod is XmlIgnore), so the preview can only say which missing
/// mod a part comes from when one mod is missing. With several missing mods, a part that does not resolve is still
/// marked as not loaded, without a mod Id.
/// </para>
/// </remarks>
public sealed class VehiclePreview : IVehiclePreview
{
    /// <summary>Goes into the scene key, so a change of the scene rules makes every stored still image old.</summary>
    internal const int FormatVersion = 2;

    private const int CachedCatalogs = 2;

    private readonly PreviewSources _sources;
    private readonly PreviewArchiveCache _archives;
    private readonly PreviewThumbnailCache? _thumbnails;
    private readonly IInstanceRepository _instances;
    private readonly IModDownloader _downloader;
    private readonly Func<ShareHost, IModDownloader>? _shareFiles;
    private readonly IFreeSpaceProbe _freeSpace;
    private readonly IBoreaLog _log;
    private readonly Lock _gate = new();
    private readonly LinkedList<(string Key, PartCatalog Catalog, MeshIndex Meshes)> _catalogs = new();
    private readonly HashSet<OwnedAssets> _open = [];
    private Task? _maintenance;
    private int _catalogBuilds;
    private bool _disposed;

    /// <param name="offlineMods">The repository over the index snapshots on disk, which never fetches. The composition passes its OfflineMods.</param>
    /// <param name="downloader">Fetches an archive into the preview cache, only when a load is allowed to.</param>
    /// <param name="thumbnails">The stills that <see cref="StartMaintenance"/> cleans up too, or null when there are none.</param>
    /// <param name="shareFiles">Makes the downloader of <see cref="FetchFromShareHostAsync"/> for one host, or null when nothing may call it.</param>
    public VehiclePreview(
        IGamePathProvider paths,
        IInstallPlanner planner,
        IModRepository offlineMods,
        IInstanceRepository instances,
        IModStateRepository modState,
        IInstalledContentRecords records,
        ModStore store,
        PreviewArchiveCache archives,
        IInstanceGameResolver games,
        IGamePlatform platform,
        IModDownloader downloader,
        IFreeSpaceProbe freeSpace,
        IBoreaLog log,
        PreviewThumbnailCache? thumbnails = null,
        Func<ShareHost, IModDownloader>? shareFiles = null)
    {
        _thumbnails = thumbnails;
        _shareFiles = shareFiles;
        ArgumentNullException.ThrowIfNull(paths);
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _archives = archives ?? throw new ArgumentNullException(nameof(archives));
        ArgumentNullException.ThrowIfNull(games);
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _freeSpace = freeSpace ?? throw new ArgumentNullException(nameof(freeSpace));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sources = new PreviewSources(paths, planner, offlineMods, instances, modState, records, store, archives, games, platform);
    }

    /// <summary>How many times a catalog was read from the mod files, so a test can show that a second load reuses it.</summary>
    internal int CatalogBuilds => Volatile.Read(ref _catalogBuilds);

    public async Task<VehiclePreviewPlan> PrepareAsync(ModVersionMetadata release, Guid? targetInstanceId, CancellationToken cancellationToken = default)
    {
        CheckVehicle(release);
        ThrowIfDisposed();

        // The awaits below often complete at once, so without this yield the file reads, the catalog build and the
        // resolve would run on the thread of the caller, which is the UI thread of the vehicle page.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        string? gameBuild = null;
        VehiclePreviewPlan Plan(VehiclePreviewPlanState state, IReadOnlyList<VehiclePreviewMissingMod>? missing = null, string? message = null) =>
            new(state, release, targetInstanceId, release.Download.SizeBytes, missing ?? [], gameBuild, message);

        try
        {
            var target = await TargetAsync(targetInstanceId).ConfigureAwait(false);
            var game = _sources.GameOf(target);
            gameBuild = PreviewGameBuild.Of(game.Version);
            if (game.Problem != KeptBuildProblem.None)
                return Plan(VehiclePreviewPlanState.Failed, message: KeptBuildMessage(game));

            switch (GameCoreFiles.Open(game.GameDirectory).Status)
            {
                case GameCoreStatus.NoGame:
                    return Plan(VehiclePreviewPlanState.NoGameFolder);
                case GameCoreStatus.GameInvalid:
                    return Plan(VehiclePreviewPlanState.GameFolderInvalid, message: "The game folder holds no Content/Core.");
            }

            var (vehicle, damaged) = await FindVehicleAsync(release, target, cancellationToken).ConfigureAwait(false);
            if (vehicle is null)
                return Plan(VehiclePreviewPlanState.VehicleDamaged, message: damaged);

            var location = vehicle.Location;
            vehicle.Dispose();

            using var mods = await _sources.PlanModsAsync(release, target, cancellationToken).ConfigureAwait(false);
            var missing = Missing(mods.Missing, null);
            return location switch
            {
                PreviewVehicleLocation.Installed or PreviewVehicleLocation.Cached => Plan(VehiclePreviewPlanState.CanBuildLocally, missing: missing),
                PreviewVehicleLocation.NeedsFetch => Plan(VehiclePreviewPlanState.NeedsVehicleFile, missing: missing),
                _ => Plan(VehiclePreviewPlanState.Failed, missing: missing, message: NoHashMessage(release)),
            };
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ObjectDisposedException))
        {
            _log.Write($"Could not prepare the preview of {release.ModId} {release.Version}.", exception);
            return Plan(VehiclePreviewPlanState.Failed, message: exception.Message);
        }
    }

    public async Task<VehiclePreviewResult> LoadAsync(
        VehiclePreviewPlan plan,
        bool fetchVehicle,
        bool fetchMods,
        IProgress<VehiclePreviewStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var release = plan.Release;
        CheckVehicle(release);
        ThrowIfDisposed();

        // The same yield as in PrepareAsync, because a load of a local vehicle can also complete every await at once.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        string? gameBuild = null;
        var sha256 = release.Download.Sha256?.Trim().ToLowerInvariant();
        VehiclePreviewResult Result(VehiclePreviewLoadState state, string? message = null) =>
            new(state, null, null, [], [], null, sha256, gameBuild, message);

        PreviewVehicleSource? vehicle = null;
        PreviewModSources? mods = null;
        try
        {
            var target = await TargetAsync(plan.TargetInstanceId).ConfigureAwait(false);
            var game = _sources.GameOf(target);
            gameBuild = PreviewGameBuild.Of(game.Version);
            if (game.Problem != KeptBuildProblem.None)
                return Result(VehiclePreviewLoadState.Failed, KeptBuildMessage(game));

            switch (GameCoreFiles.Open(game.GameDirectory).Status)
            {
                case GameCoreStatus.NoGame:
                    return Result(VehiclePreviewLoadState.NoGameFolder);
                case GameCoreStatus.GameInvalid:
                    return Result(VehiclePreviewLoadState.GameFolderInvalid, "The game folder holds no Content/Core.");
            }

            (vehicle, var damaged) = await FindVehicleAsync(release, target, cancellationToken).ConfigureAwait(false);
            if (vehicle?.Location == PreviewVehicleLocation.NeedsFetch)
            {
                if (!fetchVehicle)
                    return Result(VehiclePreviewLoadState.NeedsVehicleFile);

                progress?.Report(VehiclePreviewStage.FetchingVehicle);
                await _archives.FetchAsync(release, _downloader, _freeSpace, cancellationToken: cancellationToken).ConfigureAwait(false);
                vehicle.Dispose();
                (vehicle, damaged) = await FindVehicleAsync(release, target, cancellationToken).ConfigureAwait(false);
            }

            if (vehicle is null)
                return Result(VehiclePreviewLoadState.VehicleDamaged, damaged);

            // After a fetch the file can still be missing, when another process cleaned the cache in between.
            if (vehicle.Files is null)
                return Result(VehiclePreviewLoadState.Failed, vehicle.Location == PreviewVehicleLocation.Unavailable ? NoHashMessage(release) : "The fetched vehicle file is no longer in the preview cache.");

            progress?.Report(VehiclePreviewStage.ReadingParts);
            var (vehicleXml, unreadable) = ReadVehicleXml(vehicle.Files);
            if (vehicleXml is null)
                return Result(VehiclePreviewLoadState.VehicleDamaged, unreadable);

            mods = await _sources.PlanModsAsync(release, target, cancellationToken).ConfigureAwait(false);
            var fetchNotices = new List<PreviewNotice>();
            if (fetchMods && mods.Missing.Any(CanFetch))
            {
                progress?.Report(VehiclePreviewStage.FetchingMods);
                await FetchModsAsync(mods.Missing, fetchNotices, cancellationToken).ConfigureAwait(false);
                mods.Dispose();
                mods = null;
                progress?.Report(VehiclePreviewStage.ReadingParts);
                mods = await _sources.PlanModsAsync(release, target, cancellationToken).ConfigureAwait(false);
            }

            var ready = Resolve(vehicleXml, ref mods, ref vehicle, fetchNotices, sha256, gameBuild, cancellationToken);
            progress?.Report(VehiclePreviewStage.Done);
            return ready;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ObjectDisposedException))
        {
            _log.Write($"Could not load the preview of {release.ModId} {release.Version}.", exception);
            return Result(VehiclePreviewLoadState.Failed, exception.Message);
        }
        finally
        {
            mods?.Dispose();
            vehicle?.Dispose();
        }
    }

    public async Task<VehiclePreviewResult> LoadFolderAsync(string vehicleFolder, Guid instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleFolder);
        ThrowIfDisposed();

        // The same yield as in PrepareAsync, because a folder is read with no await that waits.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        string? gameBuild = null;
        VehiclePreviewResult Result(VehiclePreviewLoadState state, string? message = null) =>
            new(state, null, null, [], [], null, null, gameBuild, message);

        PreviewVehicleSource? vehicle = null;
        PreviewModSources? mods = null;
        try
        {
            if (await TargetAsync(instanceId).ConfigureAwait(false) is not { } target)
                return Result(VehiclePreviewLoadState.Failed, "The instance of the vehicle no longer exists.");

            var game = _sources.GameOf(target);
            gameBuild = PreviewGameBuild.Of(game.Version);
            if (game.Problem != KeptBuildProblem.None)
                return Result(VehiclePreviewLoadState.Failed, KeptBuildMessage(game));

            switch (GameCoreFiles.Open(game.GameDirectory).Status)
            {
                case GameCoreStatus.NoGame:
                    return Result(VehiclePreviewLoadState.NoGameFolder);
                case GameCoreStatus.GameInvalid:
                    return Result(VehiclePreviewLoadState.GameFolderInvalid, "The game folder holds no Content/Core.");
            }

            vehicle = new PreviewVehicleSource(PreviewVehicleLocation.Installed, new FolderPreviewFiles(vehicleFolder), vehicleFolder, null);
            var (vehicleXml, unreadable) = ReadVehicleXml(vehicle.Files!);
            if (vehicleXml is null)
                return Result(VehiclePreviewLoadState.VehicleDamaged, unreadable);

            mods = await _sources.OpenInstanceModsAsync(target, cancellationToken).ConfigureAwait(false);
            return Resolve(vehicleXml, ref mods, ref vehicle, [], null, gameBuild, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ObjectDisposedException))
        {
            _log.Write($"Could not load the preview of the vehicle folder {vehicleFolder}.", exception);
            return Result(VehiclePreviewLoadState.Failed, exception.Message);
        }
        finally
        {
            mods?.Dispose();
            vehicle?.Dispose();
        }
    }

    /// <summary>The vehicle file, or null with the reason when it is missing or holds nothing that can be read.</summary>
    private static (VehicleXml? Vehicle, string? Unreadable) ReadVehicleXml(IPreviewFiles files)
    {
        VehicleXml vehicleXml;
        using (var stream = files.TryOpen(VehicleXmlReader.FileName))
        {
            if (stream is null)
                return (null, $"The vehicle has no {VehicleXmlReader.FileName} that Borea can read.");
            vehicleXml = VehicleXmlReader.Read(stream);
        }

        return vehicleXml.Parts.Count == 0 && vehicleXml.Problems.FirstOrDefault(problem => problem.Kind == PreviewNoticeKind.AssetFailed) is { } broken
            ? (null, broken.Detail ?? $"The {VehicleXmlReader.FileName} of the vehicle cannot be read.")
            : (vehicleXml, null);
    }

    /// <summary>Places the parts and gives the ready result, which from here on owns the mod sources and the vehicle source, so the caller no longer holds them.</summary>
    private VehiclePreviewResult Resolve(VehicleXml vehicleXml, ref PreviewModSources? modSources, ref PreviewVehicleSource? vehicleSource, IReadOnlyList<PreviewNotice> fetchNotices, string? sha256, string? gameBuild, CancellationToken cancellationToken)
    {
        var (mods, vehicle) = (modSources!, vehicleSource!);
        var sourceKeys = SourceKeys(mods);
        var (catalog, meshIndex) = Catalog(gameBuild, sourceKeys, mods.Mods, cancellationToken);
        var scene = VehicleSceneResolver.Resolve(vehicleXml.Parts, catalog, meshIndex, null, vehicleXml.FuelLinks);
        if (MissingHint(scene, mods.Missing) is { } hint)
            scene = VehicleSceneResolver.Resolve(vehicleXml.Parts, catalog, meshIndex, hint, vehicleXml.FuelLinks);

        List<PreviewNotice> notices = [.. vehicleXml.Problems, .. mods.Notices, .. fetchNotices, .. catalog.Notices, .. meshIndex.Notices, .. scene.Notices];
        scene = scene with { Notices = notices };

        var assets = new OwnedAssets(this, new PreviewAssetSet(mods.Cache, mods.Mods.Select(mod => new KeyValuePair<string, IPreviewFiles>(mod.ModId, mod.Files))), mods, vehicle);
        var missing = Missing(mods.Missing, scene);
        (modSources, vehicleSource) = (null, null);
        lock (_gate)
        {
            if (_disposed)
            {
                assets.Dispose();
                throw new ObjectDisposedException(nameof(VehiclePreview));
            }

            _open.Add(assets);
        }

        return new VehiclePreviewResult(VehiclePreviewLoadState.Ready, scene, assets, missing, notices, SceneKey(gameBuild, sourceKeys), sha256, gameBuild);
    }

    public async Task FetchFromShareHostAsync(ModVersionMetadata release, ShareHost host, CancellationToken cancellationToken = default)
    {
        CheckVehicle(release);
        ThrowIfDisposed();
        if (_shareFiles is null)
            throw new InvalidOperationException("This preview has no downloader for share hosts.");

        // The same yield as in PrepareAsync, because a hit in the cache completes every await at once.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await _archives.FetchAsync(release, _shareFiles(host), _freeSpace, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task StartMaintenance()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Both sweeps log and never throw, so the task needs no error handling.
            return _maintenance ??= Task.Run(() =>
            {
                _archives.Sweep();
                _thumbnails?.Sweep();
            });
        }
    }

    public void Dispose()
    {
        List<OwnedAssets> open;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            open = [.. _open];
            _open.Clear();
            _catalogs.Clear();
        }

        foreach (var assets in open)
            assets.Dispose();
    }

    private static void CheckVehicle(ModVersionMetadata release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Type != ContentType.Vehicle)
            throw new ArgumentException($"'{release.ModId}' is a {release.Type}, and only a vehicle has a preview.", nameof(release));
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private async Task<Instance?> TargetAsync(Guid? instanceId) =>
        instanceId is { } id ? await _instances.GetByIdAsync(id).ConfigureAwait(false) : null;

    /// <summary>The vehicle source, or null with the reason when the local archive is not one the game reads as this release.</summary>
    private async Task<(PreviewVehicleSource? Source, string? Damaged)> FindVehicleAsync(ModVersionMetadata release, Instance? target, CancellationToken cancellationToken)
    {
        try
        {
            return (await _sources.FindVehicleAsync(release, target, cancellationToken).ConfigureAwait(false), null);
        }
        catch (InvalidOperationException exception)
        {
            return (null, exception.Message);
        }
    }

    private static string KeptBuildMessage(InstanceGame game) =>
        $"KSA {game.KeptBuild}, which this instance keeps, is not in its folder of the library, or that folder holds another build.";

    private static string NoHashMessage(ModVersionMetadata release) =>
        $"The release {release.ModId} {release.Version} names no SHA-256, so Borea cannot keep its file for a preview.";

    private static bool CanFetch(PreviewModNeed need) => PreviewArchiveCache.IsHash(need.Release?.Download.Sha256);

    /// <summary>Fetches each missing mod that has a release with a hash. A mod that fails stays missing, with a notice.</summary>
    private async Task FetchModsAsync(IReadOnlyList<PreviewModNeed> missing, List<PreviewNotice> notices, CancellationToken cancellationToken)
    {
        foreach (var need in missing.Where(CanFetch).DistinctBy(need => need.ModId, ModIds.Comparer))
        {
            try
            {
                await _archives.FetchAsync(need.Release!, _downloader, _freeSpace, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _log.Write($"Could not fetch {need.ModId} {need.Release!.Version} for a vehicle preview.", exception);
                notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, need.ModId, exception.Message));
            }
        }
    }

    /// <summary>
    /// One key for each mod in load order. The description of its mod.toml holds the absolute path with the length and
    /// the write time for a folder, and the archive hash for a zip, so a mod that changed on disk gets a new key.
    /// </summary>
    private static List<string> SourceKeys(PreviewModSources mods) =>
        mods.Sources.Select((source, index) => $"{source.ModId}|{source.Version}|{source.Location}|{mods.Mods[index].Files.Describe(ModFolders.DefinitionFileName) ?? source.Path}").ToList();

    private static string SceneKey(string? gameBuild, IReadOnlyList<string> sourceKeys)
    {
        var text = new StringBuilder().Append("scene-v").Append(FormatVersion).Append('\n').Append(gameBuild).Append('\n');
        foreach (var key in sourceKeys)
            text.Append(key).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private (PartCatalog Catalog, MeshIndex Meshes) Catalog(string? gameBuild, IReadOnlyList<string> sourceKeys, IReadOnlyList<CatalogMod> mods, CancellationToken cancellationToken)
    {
        var key = SceneKey(gameBuild, sourceKeys);
        lock (_gate)
        {
            for (var node = _catalogs.First; node is not null; node = node.Next)
            {
                if (node.Value.Key != key)
                    continue;

                _catalogs.Remove(node);
                _catalogs.AddFirst(node);
                return (node.Value.Catalog, node.Value.Meshes);
            }
        }

        Interlocked.Increment(ref _catalogBuilds);
        var catalog = PartCatalog.Build(mods, cancellationToken);
        var meshes = MeshIndex.Build(mods, catalog.MeshFiles, cancellationToken);
        lock (_gate)
        {
            if (!_disposed)
            {
                _catalogs.AddFirst((key, catalog, meshes));
                while (_catalogs.Count > CachedCatalogs)
                    _catalogs.RemoveLast();
            }
        }

        return (catalog, meshes);
    }

    /// <summary>
    /// The Ids that did not resolve while a needed mod is missing, each with that mod when it is the only missing one.
    /// Null when no mod is missing or everything resolved.
    /// </summary>
    private static Dictionary<string, string?>? MissingHint(ResolvedVehicle scene, IReadOnlyList<PreviewModNeed> missing)
    {
        var missingIds = missing.Select(need => need.ModId).Distinct(ModIds.Comparer).ToList();
        if (missingIds.Count == 0)
            return null;

        var modId = missingIds.Count == 1 ? missingIds[0] : null;
        var hint = new Dictionary<string, string?>(KsaIdComparer.Registry);
        foreach (var placeholder in scene.Placeholders)
        {
            if (placeholder.Reason is PlaceholderReason.UnknownPart or PlaceholderReason.UnknownSubPart or PlaceholderReason.UnknownMesh or PlaceholderReason.UnknownMaterial)
                hint.TryAdd(placeholder.MissingId, modId);
        }

        return hint.Count == 0 ? null : hint;
    }

    /// <param name="scene">The resolved scene that gives the part counts, or null before a load.</param>
    private static List<VehiclePreviewMissingMod> Missing(IReadOnlyList<PreviewModNeed> missing, ResolvedVehicle? scene) =>
        missing.DistinctBy(need => need.ModId, ModIds.Comparer)
            .Select(need => new VehiclePreviewMissingMod(
                need.ModId,
                need.Release?.Version,
                scene?.Placeholders.Count(placeholder => placeholder.Reason == PlaceholderReason.ModNotLoaded && ModIds.Equals(placeholder.ModId, need.ModId)) is { } count && count > 0 ? count : null,
                need.SizeBytes,
                CanFetch(need)))
            .ToList();

    private void Release(OwnedAssets assets)
    {
        lock (_gate)
            _open.Remove(assets);
    }

    /// <summary>
    /// The asset source of one result. It owns the mod sources, the vehicle source and their pins in the preview cache,
    /// so one dispose closes every archive the result reads.
    /// </summary>
    private sealed class OwnedAssets(VehiclePreview owner, PreviewAssetSet set, PreviewModSources mods, PreviewVehicleSource vehicle) : IPreviewAssetSource
    {
        private int _disposed;

        public Stream? TryOpenRead(PreviewAssetRef asset) => set.TryOpenRead(asset);

        public Stream? TryOpenHead(PreviewAssetRef asset) => set.TryOpenHead(asset);

        public string GetCacheKey(PreviewAssetRef asset) => set.GetCacheKey(asset);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            set.Dispose();
            mods.Dispose();
            vehicle.Dispose();
            owner.Release(this);
        }
    }
}
