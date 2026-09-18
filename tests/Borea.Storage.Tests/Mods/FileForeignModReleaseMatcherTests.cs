using System.Security.Cryptography;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileForeignModReleaseMatcherTests : IAsyncLifetime
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly ReleaseDownloader _downloader = new();
    private TestGamePathProvider _paths = null!;
    private FileInstanceRepository _instances = null!;
    private FileForeignModReleaseMatcher _matcher = null!;
    private Guid _instanceId;

    public async Task InitializeAsync()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_paths);
        _instanceId = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).Instance.InstanceId;
        var adopter = new FileForeignModAdopter(_paths, _instances, _downloader);
        _matcher = new FileForeignModReleaseMatcher(_paths, _downloader, adopter, new ReleaseSnapshots(_downloader));
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AdoptMatchingReleaseAsync_FolderHoldsAnOlderRelease_AdoptsThatRelease()
    {
        _downloader.Add("1.1.0", ("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "new"));
        _downloader.Add("1.0.0", ("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "old"));
        WriteFolder(("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "old"), ("settings.json", "{}"));

        var result = await _matcher.AdoptMatchingReleaseAsync(_instanceId, "LocalMod");

        Assert.NotNull(result);
        Assert.True(result.Matched);
        Assert.Equal(ModVersion.Parse("1.0.0"), result.InstalledMod!.Version);
        Assert.Equal(ModInstallOwnership.Foreign, result.InstalledMod.Ownership);
        Assert.Equal(2, _downloader.ArchivePaths.Count);
        Assert.All(_downloader.ArchivePaths, path => Assert.False(File.Exists(path)));
        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Equal("LocalMod", Assert.Single(saved!.Mods).ModId);
        Assert.True(File.Exists(Path.Combine(ModsFolder, "LocalMod", "settings.json")));
    }

    [Fact]
    public async Task AdoptMatchingReleaseAsync_FolderHoldsAYankedRelease_AdoptsThatRelease()
    {
        _downloader.Add("1.1.0", ("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "new"));
        _downloader.Add("1.0.0", yanked: true, ("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "old"));
        WriteFolder(("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "old"));

        var result = await _matcher.AdoptMatchingReleaseAsync(_instanceId, "LocalMod");

        Assert.True(result!.Matched);
        Assert.Equal(ModVersion.Parse("1.0.0"), result.InstalledMod!.Version);
    }

    [Fact]
    public async Task AdoptMatchingReleaseAsync_ArchiveWithARootFolder_ComparesBelowTheRoot()
    {
        _downloader.Add("1.0.0", ("LocalMod/mod.toml", "name = \"LocalMod\""), ("LocalMod/Data/parts.xml", "<Parts />"));
        WriteFolder(("mod.toml", "name = \"LocalMod\""), ("Data/parts.xml", "<Parts />"));

        var result = await _matcher.AdoptMatchingReleaseAsync(_instanceId, "localmod");

        Assert.True(result!.Matched);
    }

    [Fact]
    public async Task AdoptMatchingReleaseAsync_ChangedFile_ReturnsNullAndRecordsNothing()
    {
        _downloader.Add("1.0.0", ("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "old"));
        WriteFolder(("mod.toml", "name = \"LocalMod\""), ("LocalMod.dll", "patched"));

        var result = await _matcher.AdoptMatchingReleaseAsync(_instanceId, "LocalMod");

        Assert.Null(result);
        var saved = await _instances.GetByIdAsync(_instanceId);
        Assert.Empty(saved!.Mods);
        Assert.True(Directory.Exists(Path.Combine(ModsFolder, "LocalMod")));
    }

    [Fact]
    public async Task AdoptMatchingReleaseAsync_NotInTheIndex_ReturnsNullWithoutADownload()
    {
        WriteFolder(("mod.toml", "name = \"LocalMod\""));

        var result = await _matcher.AdoptMatchingReleaseAsync(_instanceId, "LocalMod");

        Assert.Null(result);
        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task AdoptMatchingReleaseAsync_NoFolder_Throws()
    {
        _downloader.Add("1.0.0", ("mod.toml", "name = \"LocalMod\""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _matcher.AdoptMatchingReleaseAsync(_instanceId, "LocalMod"));

        Assert.Empty(_downloader.ArchivePaths);
    }

    private string ModsFolder => _paths.GetInstanceModsFolder(_instanceId);

    private void WriteFolder(params (string Path, string Content)[] files)
    {
        foreach (var (path, content) in files)
        {
            var target = Path.Combine(ModsFolder, "LocalMod", path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
    }

    /// <summary>
    /// Serves one archive per release and finds a release by the hash of its archive.
    /// </summary>
    private sealed class ReleaseDownloader : IModDownloader, IModArchiveReleaseLookup
    {
        private readonly Dictionary<string, byte[]> _archives = new(StringComparer.Ordinal);

        public List<ModVersionMetadata> Releases { get; } = new();

        public List<string> ArchivePaths { get; } = new();

        public ModVersionMetadata Add(string version, params (string Path, string Content)[] entries)
            => Add(version, yanked: false, entries);

        public ModVersionMetadata Add(string version, bool yanked, params (string Path, string Content)[] entries)
        {
            var bytes = TestArchives.Build(entries);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var release = new ModVersionMetadata(
                specVersion: 1,
                modId: "LocalMod",
                version: ModVersion.Parse(version),
                releaseStatus: ReleaseStatus.Stable,
                releaseDate: DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
                gameMin: "2026.9.7.5402",
                gameMinRevision: 5402,
                download: new DownloadInfo($"https://example.invalid/{version}.zip", sha256, null, "application/zip"),
                installSizeBytes: null,
                dependencies: Array.Empty<Borea.Core.Dependencies.ModDependency>(),
                yanked: yanked);
            _archives[release.Download.Url] = bytes;
            Releases.Add(release);
            return release;
        }

        public async Task<DownloadResult> DownloadAsync(
            ModVersionMetadata release,
            string archivePath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArchivePaths.Add(archivePath);
            var bytes = _archives[release.Download.Url];
            await File.WriteAllBytesAsync(archivePath, bytes, cancellationToken);
            return new DownloadResult(release.Download.Url, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        }

        public Task<ModVersionMetadata?> FindBySha256Async(string modId, string sha256, CancellationToken cancellationToken = default)
            => Task.FromResult(Releases.FirstOrDefault(release =>
                ModIds.Equals(release.ModId, modId) && string.Equals(release.Download.Sha256, sha256, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class ReleaseSnapshots(ReleaseDownloader downloader) : IContentIndexSnapshotProvider
    {
        public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ContentIndexSnapshot(
                1,
                downloader.Releases.Count == 0 ? [] : [new ContentIndexListing("LocalMod", null, downloader.Releases, null)],
                [],
                null,
                []));
    }
}
