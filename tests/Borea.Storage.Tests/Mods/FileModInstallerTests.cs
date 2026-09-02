using System.IO.Compression;
using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.State;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileModInstallerTests : IAsyncLifetime
{
    private const string ModId = "test-mod";

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileInstanceRepository _instances;
    private readonly FileModStateRepository _modState;
    private readonly FakeModDownloader _downloader = new();
    private readonly FileModInstaller _installer;
    private Guid _instanceId;

    public FileModInstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_pathProvider);
        _modState = new FileModStateRepository(_pathProvider);
        _installer = new FileModInstaller(_pathProvider, _downloader, _instances, _modState, new FixedTimeProvider(Now));
    }

    public async Task InitializeAsync()
    {
        var instance = await _instances.CreateAsync("Test", InstanceSource.Custom.Value);
        _instanceId = instance.InstanceId;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    private string ModsFolder => _pathProvider.GetInstanceModsFolder(_instanceId);

    private string ModFolder => Path.Combine(ModsFolder, ModId);

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static byte[] BuildZip(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static ModVersionMetadata Release(
        InstallInfo? install = null,
        ContentType type = ContentType.Mod,
        string contentType = "application/zip",
        string? sha256 = null) => new(
        specVersion: 1,
        modId: ModId,
        version: ModVersion.Parse("1.2.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo("https://example.com/mod.zip", sha256, null, contentType),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>(),
        type: type,
        install: install);

    private static InstallInfo Root(string root) => new(root, derived: false);

    private Task<InstallResult> InstallAsync(ModVersionMetadata? release = null, bool enable = true, InstallReason reason = InstallReason.Manual) =>
        _installer.InstallAsync(_instanceId, release ?? Release(), reason, enable);

    private void WriteManifest(string toml)
    {
        var path = _pathProvider.GetInstanceManifestPath(_instanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, toml);
    }

    private async Task AssertNothingInstalledAsync()
    {
        Assert.False(Directory.Exists(ModFolder));

        var instance = await _instances.GetByIdAsync(_instanceId);
        Assert.Empty(instance!.Mods);
        Assert.Empty(await _modState.GetEntriesAsync(_instanceId));
        Assert.All(_downloader.ArchivePaths, path => Assert.False(File.Exists(path)));
    }

    #region Unpacking

    [Fact]
    public async Task InstallAsync_DerivedRoot_UnpacksUnderTheId()
    {
        _downloader.Bytes = BuildZip((ModId + "/mod.toml", "name = \"test-mod\""), (ModId + "/Plugins/plugin.dll", "dll"));

        await InstallAsync(Release(install: new InstallInfo(ModId, derived: true)));

        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "Plugins", "plugin.dll")));
        Assert.False(Directory.Exists(Path.Combine(ModFolder, ModId)));
    }

    [Fact]
    public async Task InstallAsync_AuthoredRoot_UnpacksOnlyThatDirectory()
    {
        _downloader.Bytes = BuildZip(
            ("build/Mod A/mod.toml", "name"),
            ("build/Mod A/plugin.dll", "dll"),
            ("build/other/file.txt", "outside the root"),
            ("readme.txt", "outside the root"));

        await InstallAsync(Release(install: Root("build/Mod A")));

        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "plugin.dll")));
        Assert.False(File.Exists(Path.Combine(ModFolder, "readme.txt")));
        Assert.False(Directory.Exists(Path.Combine(ModFolder, "other")));
        Assert.Equal(ModId, Path.GetFileName(Assert.Single(Directory.GetDirectories(ModsFolder))));
    }

    [Fact]
    public async Task InstallAsync_NoStatedRoot_TopLevelModToml_UnpacksTheArchiveRoot()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"), ("plugin.dll", "dll"), ("Plugins/extra.dll", "dll"));

        await InstallAsync(Release(install: null));

        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "plugin.dll")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "Plugins", "extra.dll")));
    }

    [Fact]
    public async Task InstallAsync_NoStatedRoot_OneWrappingDirectory_DerivesItAsTheRoot()
    {
        // RFC 0035 rule 9: for a mod, the one top-level directory holding mod.toml is the root.
        _downloader.Bytes = BuildZip(
            ("Mod A/mod.toml", "name"),
            ("Mod A/Plugins/plugin.dll", "dll"),
            ("README.md", "a file beside the wrapper"));

        await InstallAsync(Release(install: null));

        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "Plugins", "plugin.dll")));
        Assert.False(Directory.Exists(Path.Combine(ModFolder, "Mod A")));
        Assert.False(File.Exists(Path.Combine(ModFolder, "README.md")));
    }

    [Fact]
    public async Task InstallAsync_NoStatedRoot_WrappingDirectoryWithoutModToml_FailsAndLeavesNothing()
    {
        _downloader.Bytes = BuildZip(("Mod A/readme.txt", "no mod definition anywhere"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(Release(install: null)));

        Assert.Contains("mod.toml", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_NoStatedRoot_TwoTopLevelDirectories_DerivesNoRootAndFails()
    {
        _downloader.Bytes = BuildZip(("Mod A/mod.toml", "name"), ("Mod B/mod.toml", "name"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(Release(install: null)));

        Assert.Contains("mod.toml", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_EntriesWithALeadingDotSlash_AreUnpacked()
    {
        // zip -r on Linux stores every entry as "./...".
        _downloader.Bytes = BuildZip(("./test-mod/mod.toml", "name"), ("./test-mod/plugin.dll", "dll"));

        await InstallAsync(Release(install: Root(ModId)));

        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(ModFolder, "plugin.dll")));
    }

    [Fact]
    public async Task InstallAsync_ArchiveDirectoryDisagreesWithTheId_FolderIsNamedByTheId()
    {
        _downloader.Bytes = BuildZip(("Other Name/mod.toml", "name"));

        await InstallAsync(Release(install: Root("Other Name")));

        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
        Assert.False(Directory.Exists(Path.Combine(ModsFolder, "Other Name")));
    }

    [Fact]
    public async Task InstallAsync_RootNotInTheArchive_FailsAndLeavesNothing()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(Release(install: Root("elsewhere"))));

        Assert.Contains("elsewhere", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_NoModToml_FailsAndLeavesNothing()
    {
        _downloader.Bytes = BuildZip(("readme.txt", "no mod definition here"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Contains("mod.toml", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_EntryEscapingTheFolder_FailsAndLeavesNothing()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"), ("../escaped.txt", "outside"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.False(File.Exists(Path.Combine(ModsFolder, "escaped.txt")));
        await AssertNothingInstalledAsync();
    }

    #endregion

    #region Recording

    [Fact]
    public async Task InstallAsync_RecordsTheInstallOnTheInstance()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));
        var release = Release(sha256: Sha256Of(_downloader.Bytes));

        var result = await InstallAsync(release, reason: InstallReason.Dependency);

        var instance = await _instances.GetByIdAsync(_instanceId);
        var installed = Assert.Single(instance!.Mods);
        Assert.Equal(ModId, installed.ModId);
        Assert.Equal(ModVersion.Parse("1.2.0"), installed.Version);
        Assert.Equal(InstallReason.Dependency, installed.Reason);
        Assert.Equal(Now, installed.InstalledAt);
        Assert.Equal(Sha256Of(_downloader.Bytes), installed.Checksum);
        Assert.Equal(release.Download.Url, installed.Metadata.Download.Url);
        Assert.Equal(installed.ModId, result.Mod.ModId);
        Assert.Equal(Sha256Of(_downloader.Bytes), result.Download.Sha256);
    }

    [Fact]
    public async Task InstallAsync_ReleaseWithoutHash_RecordsTheDigestOfTheReceivedBytes()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        await InstallAsync(Release(sha256: null));

        var instance = await _instances.GetByIdAsync(_instanceId);
        Assert.Equal(Sha256Of(_downloader.Bytes), Assert.Single(instance!.Mods).Checksum);
    }

    [Fact]
    public async Task InstallAsync_HandsProgressAndTheTokenToTheDownloader()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));
        var progress = new Progress<DownloadProgress>();
        using var cancellation = new CancellationTokenSource();

        await _installer.InstallAsync(_instanceId, Release(), InstallReason.Manual, enable: true, progress, cancellation.Token);

        Assert.Same(progress, _downloader.LastProgress);
        Assert.Equal(cancellation.Token, _downloader.LastToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstallAsync_WritesTheManifestEntryAsAsked(bool enable)
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        var result = await InstallAsync(enable: enable);

        Assert.Equal(ModEntryAddResult.Added, result.ManifestEntry);
        var entry = Assert.Single(await _modState.GetEntriesAsync(_instanceId));
        Assert.Equal(new ModManifestEntry(ModId, enable), entry);
    }

    [Fact]
    public async Task InstallAsync_EntryDisabledInTheGame_StaysDisabled()
    {
        WriteManifest("""
            [[mods]]
            id = "test-mod"
            enabled = false
            """);
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        var result = await InstallAsync(enable: true);

        Assert.Equal(ModEntryAddResult.AlreadyListed, result.ManifestEntry);
        Assert.False(await _modState.IsActiveAsync(_instanceId, ModId));
    }

    [Fact]
    public async Task InstallAsync_DeletesTheTemporaryArchive()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        await InstallAsync();

        var archivePath = Assert.Single(_downloader.ArchivePaths);
        Assert.False(File.Exists(archivePath));
    }

    [Fact]
    public async Task InstallAsync_DownloadFails_LeavesNothing()
    {
        _downloader.Failure = new DownloadFailedException("no source served the archive");

        await Assert.ThrowsAsync<DownloadFailedException>(() => InstallAsync());

        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_ManifestWriteFails_RollsTheInstallBack()
    {
        var installer = new FileModInstaller(_pathProvider, _downloader, _instances, new FailingModStateRepository(), new FixedTimeProvider(Now));
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(_instanceId, Release(), InstallReason.Manual, enable: true));

        await AssertNothingInstalledAsync();
    }

    #endregion

    #region Refusals

    [Fact]
    public async Task InstallAsync_AlreadyInstalled_ThrowsBeforeDownloading()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));
        await InstallAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Single(_downloader.ArchivePaths);
        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
    }

    [Fact]
    public async Task InstallAsync_FolderBoreaDidNotInstall_Refuses()
    {
        // Same id in another case: the game would see one mod, so it is the same folder.
        var foreign = Path.Combine(ModsFolder, "Test-Mod");
        Directory.CreateDirectory(foreign);
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Empty(_downloader.ArchivePaths);
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public async Task InstallAsync_UnknownInstance_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _installer.InstallAsync(Guid.NewGuid(), Release(), InstallReason.Manual, enable: true));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_ModLoader_IsNotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => InstallAsync(Release(type: ContentType.ModLoader)));

        Assert.Empty(_downloader.ArchivePaths);
    }

    public static TheoryData<InstallInfo> DestinationsOutsideTheModsFolder => new()
    {
        new InstallInfo(null, derived: false, target: InstallAnchor.Standalone),
        new InstallInfo(null, derived: false, target: InstallAnchor.UserData),
        new InstallInfo(null, derived: false, target: InstallAnchor.GameRoot),
        new InstallInfo(null, derived: false, target: InstallAnchor.Unknown),
        new InstallInfo(null, derived: false, target: InstallAnchor.Mods, path: "nested"),
    };

    [Theory]
    [MemberData(nameof(DestinationsOutsideTheModsFolder))]
    public async Task InstallAsync_DestinationTheGameWouldNotScan_IsNotSupported(InstallInfo install)
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => InstallAsync(Release(install: install)));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_NotAZip_IsNotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => InstallAsync(Release(contentType: "application/x-7z-compressed")));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_OtherZipMediaType_Installs()
    {
        _downloader.Bytes = BuildZip(("mod.toml", "name"));

        await InstallAsync(Release(contentType: "application/x-zip-compressed"));

        Assert.True(File.Exists(Path.Combine(ModFolder, "mod.toml")));
    }

    [Fact]
    public async Task InstallAsync_NullRelease_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _installer.InstallAsync(_instanceId, null!, InstallReason.Manual, enable: true));
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileModInstaller(null!, _downloader, _instances, _modState));
        Assert.Throws<ArgumentNullException>(() => new FileModInstaller(_pathProvider, null!, _instances, _modState));
        Assert.Throws<ArgumentNullException>(() => new FileModInstaller(_pathProvider, _downloader, null!, _modState));
        Assert.Throws<ArgumentNullException>(() => new FileModInstaller(_pathProvider, _downloader, _instances, null!));
    }

    #endregion

    /// <summary>Hands out configured bytes as the archive, or fails, and remembers where it was asked to write.</summary>
    private sealed class FakeModDownloader : IModDownloader
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();

        public Exception? Failure { get; set; }

        public List<string> ArchivePaths { get; } = new();

        public IProgress<DownloadProgress>? LastProgress { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public async Task<DownloadResult> DownloadAsync(
            ModVersionMetadata release,
            string archivePath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArchivePaths.Add(archivePath);
            LastProgress = progress;
            LastToken = cancellationToken;

            if (Failure is not null)
                throw Failure;

            await File.WriteAllBytesAsync(archivePath, Bytes, cancellationToken);
            return new DownloadResult(release.Download.Url, Bytes.Length, Sha256Of(Bytes));
        }
    }

    /// <summary>A manifest that cannot be written, to exercise the rollback.</summary>
    private sealed class FailingModStateRepository : IModStateRepository
    {
        public Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default) =>
            throw new IOException("The disk is full.");

        public Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
