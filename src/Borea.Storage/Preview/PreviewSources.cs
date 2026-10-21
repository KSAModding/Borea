using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Planning;
using Borea.Core.Preview;
using Borea.Core.State;
using Borea.Core.UserContent;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Preview.Ksa;
using Borea.Storage.Preview.Sources;

namespace Borea.Storage.Preview;

/// <summary>
/// Finds the files of a vehicle and of the mods it needs on the local disk, without an install and without the network.
/// The mods come from the install planner over the offline repository, which reads only the index snapshots Borea holds,
/// and every source is opened read-only, so nothing here writes a file.
/// </summary>
/// <remarks>
/// <para>
/// A needed mod is read from the first source that has its release, from the cheapest to the most expensive one: the
/// mods folder of the target instance, the shared store entry, the mods folder of another instance with the same
/// release, and last the archive in the preview cache. A source that has another version does not count, because its
/// parts can differ from the ones the vehicle was built with.
/// </para>
/// <para>
/// The installed vehicle folder counts only when its record names this release and the folder is unchanged since the
/// install, because the player can edit the vehicle in the game and the preview shows the shared release.
/// </para>
/// </remarks>
internal sealed class PreviewSources
{
    /// <summary>The name of the instance that is never saved, which the planner gets when the preview has no target instance.</summary>
    private const string DraftName = "preview";

    private static readonly IReadOnlySet<string> NoRecommendations = new HashSet<string>(StringComparer.Ordinal);

    private readonly IGamePathProvider _paths;
    private readonly IInstallPlanner _planner;
    private readonly IModRepository _offlineMods;
    private readonly IInstanceRepository _instances;
    private readonly IModStateRepository _modState;
    private readonly IInstalledContentRecords _records;
    private readonly ModStore _store;
    private readonly PreviewArchiveCache _archives;
    private readonly IInstalledGameVersionProvider _gameVersion;
    private readonly IGamePlatform _platform;

    /// <param name="offlineMods">The repository over the index snapshots on disk, which never fetches. The composition passes its OfflineMods.</param>
    public PreviewSources(
        IGamePathProvider paths,
        IInstallPlanner planner,
        IModRepository offlineMods,
        IInstanceRepository instances,
        IModStateRepository modState,
        IInstalledContentRecords records,
        ModStore store,
        PreviewArchiveCache archives,
        IInstalledGameVersionProvider gameVersion,
        IGamePlatform platform)
    {
        _paths = paths;
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _offlineMods = offlineMods ?? throw new ArgumentNullException(nameof(offlineMods));
        _instances = instances;
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _archives = archives;
        _gameVersion = gameVersion;
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    /// <summary>
    /// The files of the vehicle release: the installed folder in <paramref name="target"/> when it holds this release
    /// unchanged, else the cached archive, else nothing and the size a fetch would download. A cached archive must
    /// pass the checks of an install (<see cref="UserContentFolder.Check(ModVersionMetadata, Func{string, bool}, Func{string, Stream?})"/>).
    /// </summary>
    /// <param name="target">The instance the player installs into, or null when there is none.</param>
    /// <exception cref="InvalidOperationException">The cached archive cannot be read, or it is not one the game reads as this release.</exception>
    public async Task<PreviewVehicleSource> FindVehicleAsync(ModVersionMetadata release, Instance? target, CancellationToken cancellationToken = default)
    {
        if (release.Type != ContentType.Vehicle)
            throw new ArgumentException($"'{release.ModId}' is a {release.Type}, and only a vehicle has a preview.", nameof(release));

        if (target is not null && await FindInstalledAsync(release, target, cancellationToken).ConfigureAwait(false) is { } folder)
            return new PreviewVehicleSource(PreviewVehicleLocation.Installed, new FolderPreviewFiles(folder), folder, release.Download.SizeBytes);

        // Without a hash a cached file could not be shown to hold this release, so the cache never keeps one.
        if (release.Download.Sha256 is not { } sha256)
            return new PreviewVehicleSource(PreviewVehicleLocation.Unavailable, null, null, release.Download.SizeBytes);

        var pin = _archives.Pin(sha256);
        if (_archives.TryGet(sha256) is not { } zip)
        {
            pin.Dispose();
            return new PreviewVehicleSource(PreviewVehicleLocation.NeedsFetch, null, null, release.Download.SizeBytes);
        }

        var cache = new PreviewEntryCache();
        ZipPreviewFiles files;
        try
        {
            files = OpenArchive(zip, release, [UserContentFolder.MetadataFileName, UserContentFolder.GameFileName(release.Type)], cache);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            pin.Dispose();
            throw new InvalidOperationException($"The cached archive of '{release.ModId}' {release.Version} cannot be read. {exception.Message}", exception);
        }

        try
        {
            // The entries are found ignoring case, so a meta.toml in another case passes here and fails at the install.
            UserContentFolder.Check(release, files.Exists, files.TryOpen);
        }
        catch
        {
            files.Dispose();
            pin.Dispose();
            throw;
        }

        return new PreviewVehicleSource(PreviewVehicleLocation.Cached, files, zip, release.Download.SizeBytes, pin, cache);
    }

    /// <summary>
    /// Plans the mods the vehicle needs against <paramref name="target"/>, or against a new instance that is never saved,
    /// and opens the source of each one that is on disk. The result holds the content mods of the game first, then the
    /// needed mods in the order of the target manifest, then the rest with dependencies before dependents and ties by
    /// ordinal id. When the planner fails, the required dependencies of the release are listed instead, so every mod the
    /// vehicle needs is still named.
    /// </summary>
    /// <param name="target">The instance the player installs into, or null when there is none.</param>
    public async Task<PreviewModSources> PlanModsAsync(ModVersionMetadata release, Instance? target, CancellationToken cancellationToken = default)
    {
        var instance = target ?? new Instance(DraftName, InstanceSource.Custom.Value);
        var others = await OtherInstancesAsync(instance.InstanceId).ConfigureAwait(false);
        var notices = new List<PreviewNotice>();
        var unsatisfied = new List<ModDependency>();
        InstallPlan plan;
        IReadOnlyList<string> needed;
        try
        {
            plan = await PlanAsync(release, instance, others, cancellationToken).ConfigureAwait(false);
            needed = UserContentMods.Needed(instance, [release], plan, unsatisfied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            notices.Add(new PreviewNotice(PreviewNoticeKind.PlannerFallback, release.ModId, exception.Message));
            plan = new InstallPlan(instance.InstanceId, InstallPlanningState.Capture(instance), [], [], [], [], [], []);
            unsatisfied.Clear();
            needed = UserContentMods.Needed(instance, [release], plan, unsatisfied);
        }

        var cache = new PreviewEntryCache();
        var pins = new List<IDisposable>();
        var mods = new List<CatalogMod>();
        var sources = new List<PreviewModSource>();
        try
        {
            var core = GameCoreFiles.Open(_paths.GetGameDirectoryPath());
            foreach (var (id, files) in core.Mods)
            {
                mods.Add(new CatalogMod(id, files));
                sources.Add(new PreviewModSource(id, null, PreviewModLocation.Game, files.Root));
            }

            var found = new List<Found>();
            var missing = new List<PreviewModNeed>();
            foreach (var id in needed)
            {
                var modRelease = Selected(plan, id) ?? instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, id))?.Metadata;
                if (Locate(id, modRelease, instance, others, cache, pins, notices) is { } located)
                    found.Add(located);
                else
                    missing.Add(new PreviewModNeed(id, modRelease));
            }

            missing.AddRange(unsatisfied.Select(dependency => new PreviewModNeed(dependency.ModId ?? dependency.AnyOf!.First().ModId, null, dependency)));
            notices.AddRange(missing.Select(need => new PreviewNotice(PreviewNoticeKind.MissingSource, need.ModId)));

            var manifest = target is null ? [] : await ManifestAsync(target.InstanceId, cancellationToken).ConfigureAwait(false);
            foreach (var item in LoadOrder(found, manifest))
            {
                mods.Add(new CatalogMod(item.Source.ModId, item.Files));
                sources.Add(item.Source);
            }

            return new PreviewModSources(core.Status, mods, sources, missing, notices, cache, pins);
        }
        catch
        {
            foreach (var mod in mods)
                mod.Files.Dispose();
            foreach (var pin in pins)
                pin.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Plans once, and once more when an any_of is left open, with the alternative that an instance already holds, or
    /// else the first one, so that the preview reads a mod that is on disk where it can.
    /// </summary>
    private async Task<InstallPlan> PlanAsync(ModVersionMetadata release, Instance instance, IReadOnlyList<Instance> others, CancellationToken cancellationToken)
    {
        var request = new InstallPlanningRequest(
            instance,
            [new RequestedMod(release, InstallReason.Manual, Exact: true)],
            _offlineMods,
            _gameVersion.GetInstalledVersion()?.Version,
            _platform.Current,
            NoRecommendations);
        var plan = await _planner.PlanAsync(request, cancellationToken).ConfigureAwait(false);

        var open = plan.Choices.Where(choice => choice.Kind == PlanningChoiceKind.Alternative && choice.Selected is null).ToList();
        if (open.Count == 0)
            return plan;

        var alternatives = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var choice in open)
        {
            if (PreferredAlternative(choice, instance, others) is { } id)
                alternatives[choice.Key] = id;
        }

        return alternatives.Count == 0
            ? plan
            : await _planner.PlanAsync(request with { Alternatives = alternatives }, cancellationToken).ConfigureAwait(false);
    }

    private static string? PreferredAlternative(PlanningChoice choice, Instance target, IReadOnlyList<Instance> others)
    {
        var options = choice.Dependency.AnyOf ?? [];
        var local = options.FirstOrDefault(option =>
            target.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, option.ModId))
            || others.Prepend(target).Any(instance => instance.Mods.Any(mod => ModIds.Equals(mod.ModId, option.ModId) && option.BoundsContain(mod.Version))));
        return (local ?? options.FirstOrDefault())?.ModId;
    }

    /// <summary>Opens the first local source of the release, or gives null when none has it.</summary>
    private Found? Locate(string modId, ModVersionMetadata? release, Instance target, IReadOnlyList<Instance> others, PreviewEntryCache cache, List<IDisposable> pins, List<PreviewNotice> notices)
    {
        var installed = target.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));
        if (TargetFolder(modId, release, installed, target) is { } own)
            return Folder(modId, release?.Version ?? installed?.Version, PreviewModLocation.InstanceFolder, own, release);

        // A store entry, another instance and the cache all name a release by its hash.
        if (release?.Download.Sha256 is not { } sha256)
            return null;

        var entry = _store.EntryPath(release.ModId, release.Version, sha256);
        if (Directory.Exists(entry))
            return Folder(modId, release.Version, PreviewModLocation.StoreEntry, entry, release);

        foreach (var other in others)
        {
            var same = other.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId) && mod.Version == release.Version && string.Equals(mod.Checksum, sha256, StringComparison.OrdinalIgnoreCase));
            if (same is not null && TryFind(() => ModFolders.FindOwned(_paths.GetInstanceModsFolder(other.InstanceId), same, _store)) is { } folder)
                return Folder(modId, release.Version, PreviewModLocation.OtherInstance, folder, release);
        }

        var pin = _archives.Pin(sha256);
        if (_archives.TryGet(sha256) is not { } zip)
        {
            pin.Dispose();
            return null;
        }

        try
        {
            var files = OpenArchive(zip, release, [ModFolders.DefinitionFileName], cache);
            pins.Add(pin);
            return new Found(new PreviewModSource(modId, release.Version, PreviewModLocation.ArchiveCache, zip), release, files);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            pin.Dispose();
            notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, modId, exception.Message));
            return null;
        }
    }

    /// <summary>
    /// The folder of the mod in the target instance, when it holds the release the plan selected. A mod Borea did not
    /// install has no release, and its folder counts when the plan selected none.
    /// </summary>
    private string? TargetFolder(string modId, ModVersionMetadata? release, InstalledMod? installed, Instance target)
    {
        var modsFolder = _paths.GetInstanceModsFolder(target.InstanceId);
        if (installed is not null)
            return release is null || installed.Version == release.Version ? TryFind(() => ModFolders.FindOwned(modsFolder, installed, _store)) : null;

        return release is null && target.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, modId))
            ? TryFind(() => ModFolders.Find(modsFolder, modId))
            : null;
    }

    private static Found Folder(string modId, ModVersion? version, PreviewModLocation location, string folder, ModVersionMetadata? release) =>
        new(new PreviewModSource(modId, version, location, folder), release, new FolderPreviewFiles(folder));

    private static ZipPreviewFiles OpenArchive(string zip, ModVersionMetadata release, IReadOnlyList<string> rootMarkers, PreviewEntryCache cache) =>
        ZipPreviewFiles.Open(zip, release.Install?.Root, rootMarkers, release.Download.Sha256, release.InstallSizeBytes, cache);

    /// <summary>The folder a lookup finds, or null when the folder cannot be read, which only means this source does not count.</summary>
    private static string? TryFind(Func<string?> find)
    {
        try
        {
            return find();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The installed folder of the release in the target instance, when its record names this release and the folder is unchanged.</summary>
    private async Task<string?> FindInstalledAsync(ModVersionMetadata release, Instance target, CancellationToken cancellationToken)
    {
        IReadOnlyList<InstalledContentRecord> records;
        try
        {
            records = await _records.ListAsync(target.InstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A record file that Borea cannot read cannot show that a folder holds this release.
            return null;
        }

        foreach (var record in records.Where(record => record.Type == release.Type && ModIds.Equals(record.ListingId, release.ModId) && IsSameRelease(record.Metadata, release)))
        {
            try
            {
                var folder = FileGameSaveStore.FindFolder(Path.Combine(UserContentFolder.KindFolder(_paths, target.InstanceId, record.Type), record.FolderName));
                if (Directory.Exists(folder) && (await _records.CompareAsync(target.InstanceId, record, cancellationToken).ConfigureAwait(false)).IsUnchanged)
                    return folder;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    /// <summary>The hashes match, ignoring case, or the versions match when the release has no hash.</summary>
    private static bool IsSameRelease(ModVersionMetadata recorded, ModVersionMetadata release) =>
        release.Download.Sha256 is { } sha256
            ? string.Equals(recorded.Download.Sha256, sha256, StringComparison.OrdinalIgnoreCase)
            : recorded.Version == release.Version;

    /// <summary>The other instances, which are only an extra source, so a failure to list them gives none.</summary>
    private async Task<IReadOnlyList<Instance>> OtherInstancesAsync(Guid targetId)
    {
        try
        {
            return (await _instances.GetAllAsync().ConfigureAwait(false)).Where(instance => instance.InstanceId != targetId).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>The manifest entries of the target, or none when the manifest cannot be read, which leaves the dependency order.</summary>
    private async Task<IReadOnlyList<ModManifestEntry>> ManifestAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        try
        {
            return await _modState.GetEntriesAsync(instanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    private static ModVersionMetadata? Selected(InstallPlan plan, string modId) =>
        plan.Selections.FirstOrDefault(selection => ModIds.Equals(selection.Release.ModId, modId))?.Release;

    /// <summary>
    /// The mods in the order of the manifest first, as the game loads them (ModLibrary.PrepareAll), then the others with
    /// each one after the mods it requires and ties by ordinal id. A cycle takes the lowest id next.
    /// </summary>
    private static List<Found> LoadOrder(List<Found> found, IReadOnlyList<ModManifestEntry> manifest)
    {
        var ordered = new List<Found>();
        var remaining = new List<Found>(found);
        foreach (var entry in manifest)
        {
            var index = remaining.FindIndex(item => ModIds.Equals(item.Source.ModId, entry.ModId));
            if (index < 0)
                continue;

            ordered.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        while (remaining.Count > 0)
        {
            var byId = remaining.OrderBy(item => item.Source.ModId, StringComparer.Ordinal).ToList();
            var next = byId.FirstOrDefault(item => !remaining.Any(other => !ReferenceEquals(other, item) && Requires(item.Release, other.Source.ModId))) ?? byId[0];
            ordered.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }

    private static bool Requires(ModVersionMetadata? release, string modId) =>
        release is not null && release.Dependencies.Any(dependency => dependency.Kind == ModDependencyKind.Required
            && (dependency.IsAnyOf ? dependency.AnyOf!.Any(option => ModIds.Equals(option.ModId, modId)) : ModIds.Equals(dependency.ModId, modId)));

    private sealed record Found(PreviewModSource Source, ModVersionMetadata? Release, IPreviewFiles Files);
}

/// <summary>Where the preview reads the vehicle archive from.</summary>
internal enum PreviewVehicleLocation
{
    /// <summary>The unchanged folder of this release in the target instance.</summary>
    Installed,

    /// <summary>The archive in the preview cache, read in place.</summary>
    Cached,

    /// <summary>No local copy. A fetch into the preview cache gives one.</summary>
    NeedsFetch,

    /// <summary>No local copy, and the release has no hash, so the preview cache cannot keep one.</summary>
    Unavailable,
}

/// <summary>
/// The vehicle files of one preview. <see cref="Files"/> is null unless the vehicle is installed or cached, and
/// <see cref="SizeBytes"/> is the download size the release states. Dispose closes the files and lets the cache evict
/// the archive again.
/// </summary>
internal sealed class PreviewVehicleSource : IDisposable
{
    private readonly IDisposable? _pin;
    private readonly PreviewEntryCache? _cache;

    public PreviewVehicleSource(PreviewVehicleLocation location, IPreviewFiles? files, string? path, long? sizeBytes, IDisposable? pin = null, PreviewEntryCache? cache = null)
    {
        Location = location;
        Files = files;
        Path = path;
        SizeBytes = sizeBytes;
        _pin = pin;
        _cache = cache;
    }

    public PreviewVehicleLocation Location { get; }

    public IPreviewFiles? Files { get; }

    /// <summary>The vehicle folder or the cached zip file.</summary>
    public string? Path { get; }

    public long? SizeBytes { get; }

    public void Dispose()
    {
        Files?.Dispose();
        _pin?.Dispose();
        _cache?.Clear();
    }
}

/// <summary>
/// The mod sources of one preview. <see cref="Mods"/> and <see cref="Sources"/> have the same order, and the zip sources
/// among them share <see cref="Cache"/>. Dispose closes every source and lets the cache evict the archives again.
/// </summary>
internal sealed class PreviewModSources : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _pins;
    private int _disposed;

    public PreviewModSources(
        GameCoreStatus coreStatus,
        IReadOnlyList<CatalogMod> mods,
        IReadOnlyList<PreviewModSource> sources,
        IReadOnlyList<PreviewModNeed> missing,
        IReadOnlyList<PreviewNotice> notices,
        PreviewEntryCache cache,
        IReadOnlyList<IDisposable> pins)
    {
        CoreStatus = coreStatus;
        Mods = mods;
        Sources = sources;
        Missing = missing;
        Notices = notices;
        Cache = cache;
        _pins = pins;
    }

    /// <summary>Whether the game folder gave its content mods. The rest of the result is there either way.</summary>
    public GameCoreStatus CoreStatus { get; }

    /// <summary>The mods in load order, for <see cref="PartCatalog.Build"/>.</summary>
    public IReadOnlyList<CatalogMod> Mods { get; }

    /// <summary>Where each mod of <see cref="Mods"/> comes from, at the same index.</summary>
    public IReadOnlyList<PreviewModSource> Sources { get; }

    /// <summary>The needed mods that no local source has, in the order the plan names them.</summary>
    public IReadOnlyList<PreviewModNeed> Missing { get; }

    public IReadOnlyList<PreviewNotice> Notices { get; }

    public PreviewEntryCache Cache { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var mod in Mods)
            mod.Files.Dispose();
        foreach (var pin in _pins)
            pin.Dispose();
        Cache.Clear();
    }
}
