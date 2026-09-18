using System.Security.Cryptography;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.State;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileSharedProfileImporterTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly ReleaseServer _server = new();
    private readonly TestGamePathProvider _paths;
    private readonly FileInstanceRepository _instances;
    private readonly FileModStateRepository _modState;
    private readonly FileSharedProfileImporter _importer;

    public FileSharedProfileImporterTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_paths);
        _modState = new FileModStateRepository(_paths);
        var adopter = new FileForeignModAdopter(_paths, _instances, _server);
        var matcher = new FileForeignModReleaseMatcher(_paths, _server, adopter, _server);
        _importer = new FileSharedProfileImporter(_paths, _instances, _modState, adopter, matcher);
    }

    private string Profile => _paths.GetSharedProfileRoot();

    [Fact]
    public async Task GetModsAsync_KeepsTheManifestOrderAndEnabledState_AndAddsUnlistedFoldersDisabled()
    {
        WriteManifest(("Core", true), ("Zeta", true), ("Gone", true), ("Alpha", false));
        WriteMod("Alpha");
        WriteMod("Unlisted");
        WriteMod("Zeta");
        Directory.CreateDirectory(Path.Combine(Profile, "mods", "NotAMod"));

        var mods = await _importer.GetModsAsync();

        Assert.Equal(
            new[] { new SharedProfileMod("Zeta", true), new SharedProfileMod("Alpha", false), new SharedProfileMod("Unlisted", false) },
            mods);
    }

    [Fact]
    public async Task GetModsAsync_NoProfile_ReturnsNothing()
    {
        Assert.Empty(await _importer.GetModsAsync());
    }

    [Fact]
    public async Task ImportAsync_WritesTheManifestEntriesInTheSameOrderWithTheSameEnabledState()
    {
        WriteManifest(("Zeta", true), ("Alpha", false));
        WriteMod("Alpha");
        WriteMod("Unlisted");
        WriteMod("Zeta");

        var result = await _importer.ImportAsync("Main");

        Assert.Equal(
            new[] { new ModManifestEntry("Zeta", true), new ModManifestEntry("Alpha", false), new ModManifestEntry("Unlisted", false) },
            await _modState.GetEntriesAsync(result.Instance.InstanceId));
        Assert.Equal(new[] { "Zeta", "Alpha", "Unlisted" }, result.Mods.Select(mod => mod.FolderName));
        Assert.All(result.Mods, mod => Assert.True(mod.HasManifestEntry));
        Assert.True(File.Exists(Path.Combine(_paths.GetInstanceModsFolder(result.Instance.InstanceId), "Zeta", "mod.toml")));
    }

    [Fact]
    public async Task ImportAsync_CopyMatchingARelease_IsRecordedAsThatReleaseWithForeignOwnership()
    {
        _server.Add("Matched", "1.0.0", ("mod.toml", "name = \"Matched\""), ("Matched.dll", "code"));
        WriteMod("Matched", ("Matched.dll", "code"), ("settings.json", "{}"));

        var result = await _importer.ImportAsync("Main");

        var imported = Assert.Single(result.Mods);
        Assert.Equal(ModVersion.Parse("1.0.0"), imported.Release?.Version);
        Assert.Null(imported.MatchError);
        var recorded = Assert.Single(result.Instance.Mods);
        Assert.Equal(ModInstallOwnership.Foreign, recorded.Ownership);
        Assert.False(recorded.CanDeleteFiles);
        Assert.Empty(result.Instance.ForeignMods);
    }

    [Fact]
    public async Task ImportAsync_CopyMatchingNoRelease_StaysAManualInstall()
    {
        _server.Add("Changed", "1.0.0", ("mod.toml", "name = \"Changed\""), ("Changed.dll", "code"));
        WriteMod("Changed", ("Changed.dll", "patched"));
        WriteMod("LocalOnly");

        var result = await _importer.ImportAsync("Main");

        Assert.All(result.Mods, mod => Assert.Null(mod.Release));
        Assert.All(result.Mods, mod => Assert.Null(mod.MatchError));
        Assert.Empty(result.Instance.Mods);
        Assert.Equal(new[] { "Changed", "LocalOnly" }, result.Instance.ForeignMods.Select(mod => mod.FolderName));
    }

    [Fact]
    public async Task ImportAsync_LeavesTheSharedProfileUnchangedByteForByte()
    {
        _server.Add("Matched", "1.0.0", ("mod.toml", "name = \"Matched\""), ("Data/parts.xml", "<Parts />"));
        WriteManifest(("Matched", true), ("LocalOnly", false));
        WriteMod("Matched", ("Data/parts.xml", "<Parts />"));
        WriteMod("LocalOnly", ("settings.json", "{}"));
        var before = Snapshot(Profile);

        var result = await _importer.ImportAsync("Main");

        Assert.Single(result.Instance.Mods);
        Assert.Equal(before, Snapshot(Profile));
    }

    [Fact]
    public async Task ImportAsync_EmptyProfile_ThrowsAndCreatesNoInstance()
    {
        WriteManifest(("Core", true));
        Directory.CreateDirectory(Path.Combine(Profile, "mods", "NotAMod"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync("Main"));

        Assert.Empty(await _instances.GetAllAsync());
    }

    [Fact]
    public async Task ImportAsync_NameTaken_ThrowsAndCopiesNothing()
    {
        var existing = (await _instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        WriteMod("LocalOnly");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync("main"));

        Assert.Contains("already in use", exception.Message);
        Assert.Equal(existing.InstanceId, Assert.Single(await _instances.GetAllAsync()).InstanceId);
        Assert.False(Directory.Exists(_paths.GetInstanceModsFolder(existing.InstanceId)));
    }

    [Fact]
    public async Task ImportAsync_DownloadFails_KeepsTheCopyAsAManualInstallWithTheReason()
    {
        _server.Add("Listed", "1.0.0", ("mod.toml", "name = \"Listed\""));
        _server.Failure = new HttpRequestException("The host is offline.");
        WriteMod("Listed");

        var result = await _importer.ImportAsync("Main");

        var imported = Assert.Single(result.Mods);
        Assert.Null(imported.Release);
        Assert.Equal("The host is offline.", imported.MatchError);
        Assert.Equal("Listed", Assert.Single(result.Instance.ForeignMods).FolderName);
    }

    [Fact]
    public async Task ImportAsync_CancelledWhileCheckingReleases_LeavesNoInstanceAndNoActiveInstance()
    {
        using var cancellation = new CancellationTokenSource();
        _server.Add("Listed", "1.0.0", ("mod.toml", "name = \"Listed\""));
        _server.Downloading = cancellation.Cancel;
        WriteMod("Listed");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _importer.ImportAsync("Main", cancellation.Token));

        Assert.Empty(await _instances.GetAllAsync());
        Assert.Empty(Directory.EnumerateDirectories(_paths.GetInstancesRoot()));
        Assert.False(File.Exists(_paths.GetActiveInstancePointerPath()));
    }

    [Fact]
    public async Task ImportAsync_FolderNameThatIsNotAContentId_IsCopiedWithoutAManifestEntry()
    {
        WriteManifest(("My Mod", true));
        WriteMod("My Mod");

        var result = await _importer.ImportAsync("Main");

        Assert.False(Assert.Single(result.Mods).HasManifestEntry);
        Assert.Empty(await _modState.GetEntriesAsync(result.Instance.InstanceId));
        Assert.True(File.Exists(Path.Combine(_paths.GetInstanceModsFolder(result.Instance.InstanceId), "My Mod", "mod.toml")));
    }

    [Fact]
    public async Task ImportAsync_OwnershipMarkerInTheProfile_IsNotCopied()
    {
        WriteMod("LocalOnly", (".borea-owner", "token"));

        var result = await _importer.ImportAsync("Main");

        var copy = Path.Combine(_paths.GetInstanceModsFolder(result.Instance.InstanceId), "LocalOnly");
        Assert.True(File.Exists(Path.Combine(copy, "mod.toml")));
        Assert.False(File.Exists(Path.Combine(copy, ".borea-owner")));
    }

    private void WriteManifest(params (string Id, bool Enabled)[] entries)
    {
        Directory.CreateDirectory(Profile);
        File.WriteAllText(
            Path.Combine(Profile, "manifest.toml"),
            string.Concat(entries.Select(entry => $"[[mods]]\nid = \"{entry.Id}\"\nenabled = {(entry.Enabled ? "true" : "false")}\n\n")));
    }

    private void WriteMod(string folderName, params (string Path, string Content)[] files)
    {
        foreach (var (path, content) in files.Prepend(("mod.toml", $"name = \"{folderName}\"")))
        {
            var target = Path.Combine(Profile, "mods", folderName, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
    }

    private static List<string> Snapshot(string folder)
        => Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(path => File.Exists(path)
                ? $"{Path.GetRelativePath(folder, path)} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}"
                : Path.GetRelativePath(folder, path) + Path.DirectorySeparatorChar)
            .Order(StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// Serves one archive per release, finds a release by the hash of its
    /// archive, and lists the releases as the content index.
    /// </summary>
    private sealed class ReleaseServer : IModDownloader, IModArchiveReleaseLookup, IContentIndexSnapshotProvider
    {
        private readonly Dictionary<string, byte[]> _archives = new(StringComparer.Ordinal);
        private readonly List<ModVersionMetadata> _releases = [];

        public Exception? Failure { get; set; }

        public Action? Downloading { get; set; }

        public void Add(string modId, string version, params (string Path, string Content)[] entries)
        {
            var bytes = TestArchives.Build(entries);
            var release = new ModVersionMetadata(
                specVersion: 1,
                modId: modId,
                version: ModVersion.Parse(version),
                releaseStatus: ReleaseStatus.Stable,
                releaseDate: DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
                gameMin: "2026.9.7.5402",
                gameMinRevision: 5402,
                download: new DownloadInfo($"https://example.invalid/{modId}/{version}.zip", Convert.ToHexString(SHA256.HashData(bytes)), null, "application/zip"),
                installSizeBytes: null,
                dependencies: Array.Empty<Borea.Core.Dependencies.ModDependency>());
            _archives[release.Download.Url] = bytes;
            _releases.Add(release);
        }

        public async Task<DownloadResult> DownloadAsync(
            ModVersionMetadata release,
            string archivePath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Downloading?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
                throw Failure;

            var bytes = _archives[release.Download.Url];
            await File.WriteAllBytesAsync(archivePath, bytes, cancellationToken);
            return new DownloadResult(release.Download.Url, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        }

        public Task<ModVersionMetadata?> FindBySha256Async(string modId, string sha256, CancellationToken cancellationToken = default)
            => Task.FromResult(_releases.FirstOrDefault(release =>
                ModIds.Equals(release.ModId, modId) && string.Equals(release.Download.Sha256, sha256, StringComparison.OrdinalIgnoreCase)));

        public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ContentIndexSnapshot(
                1,
                _releases.GroupBy(release => release.ModId).Select(group => new ContentIndexListing(group.Key, null, group.ToList(), null)).ToList(),
                [],
                null,
                []));
    }
}
