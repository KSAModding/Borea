using System.Collections.Concurrent;
using Borea.Core.Files;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Files;
using Borea.Storage.Toml;

namespace Borea.Storage.Mods;

/// <summary>
/// The mod releases that instances share. A release is unpacked once into
/// Static Mod Files/&lt;Mod&gt;/&lt;Version&gt;-&lt;first 12 hex of its SHA-256&gt;, an
/// instance links its mod folder to that entry, and the entry goes when no
/// link in any instance leads to it any more. A mod that claims paths it
/// rewrites, a mod that was broken out of the store, and a release the
/// filesystem cannot link get a private copy instead, because a shared folder
/// shows every write to every instance. Beside each entry is the snapshot
/// that tells whether it changed since it was stored.
/// </summary>
public sealed class ModStore
{
    private const int DigestLength = 12;
    private const string TrashPrefix = ".borea-trash-";
    private const string SnapshotExtension = ".snapshot.toml";
    private const string PrivateModsFileName = "private-mods.toml";

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly IGamePathProvider _paths;
    private readonly IDirectoryLinker _linker;
    private readonly bool _linksReleases;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _modLocks = new(PathComparer);
    private readonly SemaphoreSlim _privateModsLock = new(1, 1);

    /// <param name="linksReleases">
    /// Whether a new install links to the store. Off copies every release into
    /// its instance, and the mods that are linked already are still removed and
    /// replaced correctly.
    /// </param>
    public ModStore(IGamePathProvider paths, IDirectoryLinker linker, bool linksReleases)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _linker = linker ?? throw new ArgumentNullException(nameof(linker));
        _linksReleases = linksReleases;
    }

    internal string EntryPath(InstalledMod mod) => EntryPath(mod.ModId, mod.Version, mod.Checksum!);

    internal string EntryPath(string modId, ModVersion version, string sha256)
        => Path.GetFullPath(Path.Combine(_paths.GetStaticModFilesRoot(), modId, $"{version}-{sha256[..DigestLength].ToLowerInvariant()}"));

    internal bool IsLinkTo(string path, string entry)
        => _linker.GetTarget(path) is { } target && SamePath(FullPath(target, path), entry);

    internal bool IsStoredIn(InstalledMod mod, string entry)
        => mod.Storage == ModStorage.Linked && SamePath(EntryPath(mod), entry);

    /// <summary>
    /// Downloads the release when it is not stored yet and puts it at
    /// <paramref name="stagingFolder"/>, as a link to its entry or as a private
    /// copy with a new ownership marker, so the caller only moves that folder
    /// into the mods folder. The link at the staging folder already counts as a
    /// reference, so the entry cannot go before the move.
    /// </summary>
    internal async Task<StagedRelease> StageAsync(
        IModDownloader downloader,
        ModVersionMetadata release,
        string stagingFolder,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
        try
        {
            if (!await ShouldLinkAsync(release, cancellationToken).ConfigureAwait(false))
            {
                var download = await downloader.DownloadAsync(release, archivePath, progress.ForDownload(release), cancellationToken).ConfigureAwait(false);
                progress.Report(release, InstallPhase.Extracting);
                FileModInstaller.Unpack(archivePath, release, stagingFolder);
                progress.Report(release, InstallPhase.Finishing);
                return await MarkPrivateAsync(stagingFolder, download, cancellationToken).ConfigureAwait(false);
            }

            // Without a published hash only the bytes can name the entry.
            DownloadResult? downloaded = release.Download.Sha256 is null
                ? await downloader.DownloadAsync(release, archivePath, progress.ForDownload(release), cancellationToken).ConfigureAwait(false)
                : null;
            var sha256 = downloaded?.Sha256 ?? release.Download.Sha256!;
            var entry = EntryPath(release.ModId, release.Version, sha256);

            var gate = ModLock(entry);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!Directory.Exists(entry))
                {
                    downloaded ??= await downloader.DownloadAsync(release, archivePath, progress.ForDownload(release), cancellationToken).ConfigureAwait(false);
                    progress.Report(release, InstallPhase.Extracting);
                    await AddAsync(archivePath, release, entry, cancellationToken).ConfigureAwait(false);
                }

                progress.Report(release, InstallPhase.Finishing);
                var result = downloaded ?? new DownloadResult(release.Download.Url, 0, sha256);
                if (_linker.TryCreate(stagingFolder, entry).Linked)
                    return new StagedRelease(result, ModStorage.Linked, null);

                try
                {
                    ModFolders.CopyWithoutMarker(entry, stagingFolder, cancellationToken);
                }
                finally
                {
                    DeleteIfUnused(entry);
                }

                return await MarkPrivateAsync(stagingFolder, result, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    /// <summary>Removes the stored release of a linked mod when no instance links to it any more.</summary>
    internal Task ReleaseAsync(InstalledMod mod)
        => mod.Storage == ModStorage.Linked ? ReleaseAsync([EntryPath(mod)]) : Task.CompletedTask;

    internal async Task ReleaseAsync(IEnumerable<string> entries)
    {
        foreach (var entry in entries)
        {
            var gate = ModLock(entry);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                DeleteIfUnused(entry);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>The store entries that the links in one instance lead to.</summary>
    internal IReadOnlyList<string> EntriesLinkedFrom(Guid instanceId)
    {
        return LinkTargets(instanceId)
            .Where(IsEntry)
            .Distinct(PathComparer)
            .ToList();
    }

    /// <summary>Every entry in the store.</summary>
    internal IReadOnlyList<string> Entries()
        => Children(_paths.GetStaticModFilesRoot())
            .Where(modFolder => !Path.GetFileName(modFolder).StartsWith(".borea-", StringComparison.Ordinal))
            .SelectMany(Children)
            .Select(Path.GetFullPath)
            .ToList();

    /// <summary>
    /// Whether the entry differs from the snapshot taken when it was stored. An
    /// entry without a readable snapshot cannot be shown to be unchanged, so it
    /// counts as changed. A missing entry has nothing left to break out.
    /// </summary>
    internal async Task<bool> HasChangedAsync(string entry, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(entry))
            return false;

        try
        {
            var stored = await StoredReleaseSnapshot.ReadAsync(SnapshotPath(entry), cancellationToken).ConfigureAwait(false);
            return stored is null || !stored.Matches(StoredReleaseSnapshot.Measure(entry));
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>Records that the mod is copied into each instance from now on. False when it was already.</summary>
    internal async Task<bool> MakePrivateAsync(string modId, CancellationToken cancellationToken)
    {
        await _privateModsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var privateMods = await ReadPrivateModsAsync(cancellationToken).ConfigureAwait(false);
            if (privateMods.ModIds.Contains(modId, ModIds.Comparer))
                return false;

            privateMods.ModIds.Add(modId);
            await TomlFileStore.WriteAsync(PrivateModsPath, privateMods, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _privateModsLock.Release();
        }
    }

    /// <summary>
    /// A mod that claims paths it rewrites needs a folder of its own, and so
    /// does one that was broken out. An empty claim says that there are none.
    /// </summary>
    private async Task<bool> ShouldLinkAsync(ModVersionMetadata release, CancellationToken cancellationToken)
        => _linksReleases
            && release.Manages is not { Count: > 0 }
            && !(await ReadPrivateModsAsync(cancellationToken).ConfigureAwait(false)).ModIds.Contains(release.ModId, ModIds.Comparer);

    private string PrivateModsPath => Path.Combine(_paths.GetStaticModFilesRoot(), PrivateModsFileName);

    private async Task<PrivateModsDto> ReadPrivateModsAsync(CancellationToken cancellationToken)
        => await TomlFileStore.ReadAsync<PrivateModsDto>(PrivateModsPath, cancellationToken).ConfigureAwait(false) ?? new PrivateModsDto();

    private static string SnapshotPath(string entry) => entry + SnapshotExtension;

    /// <summary>
    /// Unpacks beside the entry first, so an entry is only ever there complete,
    /// and never without its snapshot.
    /// </summary>
    private async Task AddAsync(string archivePath, ModVersionMetadata release, string entry, CancellationToken cancellationToken)
    {
        var root = _paths.GetStaticModFilesRoot();
        if (Directory.Exists(root))
        {
            foreach (var trash in Directory.EnumerateDirectories(root, TrashPrefix + "*"))
                TryDeleteDirectory(trash);
        }

        var staging = Path.Combine(root, $".borea-staging-{Guid.NewGuid():N}");
        try
        {
            FileModInstaller.Unpack(archivePath, release, staging);
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            await StoredReleaseSnapshot.Measure(staging).WriteAsync(SnapshotPath(entry), cancellationToken).ConfigureAwait(false);
            Directory.Move(staging, entry);
        }
        catch
        {
            TryDeleteFile(SnapshotPath(entry));
            throw;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static async Task<StagedRelease> MarkPrivateAsync(string folder, DownloadResult download, CancellationToken cancellationToken)
    {
        var ownershipToken = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(folder, ModFolders.OwnershipFileName), ownershipToken, cancellationToken).ConfigureAwait(false);
        return new StagedRelease(download, ModStorage.Private, ownershipToken);
    }

    /// <summary>
    /// The caller holds the lock of the mod. The entry is moved out before it is
    /// deleted, so a delete that stops part way never leaves a partial entry
    /// that a later install would link to. An entry that cannot be moved stays
    /// complete, and trash that cannot be deleted goes with the next add.
    /// </summary>
    private void DeleteIfUnused(string entry)
    {
        try
        {
            if (!IsEntry(entry) || !Directory.Exists(entry) || InstanceIds().Any(id => LinkTargets(id).Any(target => SamePath(target, entry))))
                return;

            var trash = Path.Combine(_paths.GetStaticModFilesRoot(), TrashPrefix + Guid.NewGuid().ToString("N"));
            Directory.Move(entry, trash);
            File.Delete(SnapshotPath(entry));
            var modFolder = Path.GetDirectoryName(entry)!;
            if (!Directory.EnumerateFileSystemEntries(modFolder).Any())
                Directory.Delete(modFolder);

            DirectoryLinks.DeleteTreeWithoutFollowingLinks(trash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Whether the path is a release folder below the folder of its mod in the store, and not anything above or beside it.</summary>
    private bool IsEntry(string path)
        => Path.GetDirectoryName(Path.GetDirectoryName(path)) is { } parent
            && SamePath(parent, Path.GetFullPath(_paths.GetStaticModFilesRoot()));

    /// <summary>
    /// Where the links in an instance lead: its mod folders, and the staging
    /// and recovery folders beside them while an install or replacement runs.
    /// </summary>
    private IEnumerable<string> LinkTargets(Guid instanceId)
        => Children(_paths.GetInstanceRoot(instanceId))
            .Concat(Children(_paths.GetInstanceModsFolder(instanceId)))
            .Select(link => _linker.GetTarget(link) is { } target ? FullPath(target, link) : null)
            .OfType<string>();

    private IEnumerable<Guid> InstanceIds()
        => Children(_paths.GetInstancesRoot())
            .Select(folder => Guid.TryParse(Path.GetFileName(folder), out var id) ? id : (Guid?)null)
            .OfType<Guid>();

    /// <summary>
    /// Installs of different releases of one mod share a lock, so the folder of
    /// the mod never goes while an entry is added to it.
    /// </summary>
    private SemaphoreSlim ModLock(string entry)
        => _modLocks.GetOrAdd(Path.GetDirectoryName(entry)!, static _ => new SemaphoreSlim(1, 1));

    private static IEnumerable<string> Children(string folder)
        => Directory.Exists(folder) ? Directory.EnumerateDirectories(folder) : [];

    private static string FullPath(string target, string link)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(target, Path.GetDirectoryName(link)!));

    private static bool SamePath(string left, string right)
        => PathComparer.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DirectoryLinks.DeleteTreeWithoutFollowingLinks(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A release made ready beside the mods folder, with the facts its record needs.</summary>
internal sealed record StagedRelease(DownloadResult Download, ModStorage Storage, string? OwnershipToken);
