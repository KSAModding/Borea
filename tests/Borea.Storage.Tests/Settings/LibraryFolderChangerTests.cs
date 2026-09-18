using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.Instances;
using Borea.Storage.Paths;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Launch;

namespace Borea.Storage.Tests.Settings;

public sealed class LibraryFolderChangerTests : IDisposable
{
    private static readonly DateTime ModWrittenAt = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FakeLauncher _launcher = new();
    private bool _gameProcessRunning;
    private bool _boreaRunning;

    private string BoreaRoot => Path.Combine(_tempRoot, "Borea");

    private string SharedProfile => Path.Combine(_tempRoot, "Profile");

    private string Target => Path.Combine(_tempRoot, "Library");

    [Fact]
    public async Task ChangeAsync_SameVolume_RenamesTheLibraryAndSavesTheSetting()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var reports = new List<LibraryMoveProgress>();

        var result = await Changer(sameVolume: true).ChangeAsync(Target, new SynchronousProgress<LibraryMoveProgress>(reports.Add));

        Assert.Equal(LibraryFolderChangeOutcome.Moved, result.Outcome);
        Assert.Equal(BoreaRoot, result.PreviousFolder);
        Assert.Empty(reports);
        await AssertLibraryAtAsync(Target, instance);
        Assert.False(Directory.Exists(Path.Combine(BoreaRoot, "Instances")));
        Assert.False(Directory.Exists(Path.Combine(BoreaRoot, "Backups")));
        Assert.Equal(Target, (await SettingsRepository().GetAsync())!.LibraryFolderPath);
    }

    [Fact]
    public async Task ChangeAsync_OtherVolume_CopiesComparesAndDeletesTheOldFolders()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var reports = new List<LibraryMoveProgress>();

        var result = await Changer(sameVolume: false).ChangeAsync(Target, new SynchronousProgress<LibraryMoveProgress>(reports.Add));

        Assert.Equal(LibraryFolderChangeOutcome.Moved, result.Outcome);
        Assert.False(result.OldFilesRemain);
        await AssertLibraryAtAsync(Target, instance);
        Assert.Equal(ModWrittenAt, File.GetLastWriteTimeUtc(ModFile(Target, instance)));
        Assert.False(Directory.Exists(Path.Combine(BoreaRoot, "Instances")));
        Assert.False(Directory.Exists(Path.Combine(BoreaRoot, "Backups")));
        Assert.Equal(Target, (await SettingsRepository().GetAsync())!.LibraryFolderPath);

        var copied = reports.Last(report => report.Stage == LibraryMoveStage.Copying);
        Assert.Equal(3, copied.TotalFiles);
        Assert.Equal(copied.TotalFiles, copied.Files);
        Assert.Equal(copied.TotalBytes, copied.Bytes);
        Assert.Equal(LibraryMoveStage.RemovingOldFiles, reports[^1].Stage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangeAsync_SettingsSaveFails_KeepsTheOldLibrary(bool sameVolume)
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var changer = Changer(sameVolume, new FailingSaveRepository(SettingsRepository()));

        await Assert.ThrowsAsync<IOException>(() => changer.ChangeAsync(Target));

        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public async Task ChangeAsync_CopyFails_KeepsTheOldLibraryAndLeavesNoPartialCopy()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var failing = new SynchronousProgress<LibraryMoveProgress>(report =>
        {
            if (report.Files > 0)
                throw new IOException("The disk is full.");
        });

        await Assert.ThrowsAsync<IOException>(() => Changer(sameVolume: false).ChangeAsync(Target, failing));

        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.False(Directory.Exists(Target));
        Assert.Null((await SettingsRepository().GetAsync())?.LibraryFolderPath);
    }

    [Fact]
    public async Task ChangeAsync_CancelledDuringTheCopy_KeepsTheOldLibraryAndLeavesNoPartialCopy()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        using var cancellation = new CancellationTokenSource();
        var cancelling = new SynchronousProgress<LibraryMoveProgress>(report =>
        {
            if (report.Files > 0)
                cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Changer(sameVolume: false).ChangeAsync(Target, cancelling, cancellation.Token));

        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.False(Directory.Exists(Target));
        Assert.Null((await SettingsRepository().GetAsync())?.LibraryFolderPath);
    }

    [Fact]
    public async Task ChangeAsync_RenameFailsOnTheSameVolume_CopiesInstead()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var reports = new List<LibraryMoveProgress>();
        var changer = Changer(sameVolume: true, moveDirectory: (_, _) => throw new IOException("The folder is a mount point."));

        var result = await changer.ChangeAsync(Target, new SynchronousProgress<LibraryMoveProgress>(reports.Add));

        Assert.Equal(LibraryFolderChangeOutcome.Moved, result.Outcome);
        Assert.Contains(reports, report => report.Stage == LibraryMoveStage.Copying);
        await AssertLibraryAtAsync(Target, instance);
        Assert.False(Directory.Exists(Path.Combine(BoreaRoot, "Instances")));
        Assert.Equal(Target, (await SettingsRepository().GetAsync())!.LibraryFolderPath);
    }

    [Fact]
    public async Task ChangeAsync_OldFileCannotBeDeletedAfterTheCopy_KeepsTheNewLibraryAndSaysSo()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var oldFile = ModFile(BoreaRoot, instance);
        FileStream? held = null;
        var holding = new SynchronousProgress<LibraryMoveProgress>(report =>
        {
            if (report.Stage != LibraryMoveStage.RemovingOldFiles)
                return;

            if (OperatingSystem.IsWindows())
                held = new FileStream(oldFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            else
                SetWritable(Path.GetDirectoryName(oldFile)!, writable: false);
        });

        LibraryFolderChangeResult result;
        try
        {
            result = await Changer(sameVolume: false).ChangeAsync(Target, holding);
        }
        finally
        {
            held?.Dispose();
            SetWritable(Path.GetDirectoryName(oldFile)!, writable: true);
        }

        Assert.Equal(LibraryFolderChangeOutcome.Moved, result.Outcome);
        Assert.True(result.OldFilesRemain);
        Assert.Contains(BoreaRoot, result.Message);
        Assert.True(File.Exists(oldFile));
        await AssertLibraryAtAsync(Target, instance);
        Assert.Equal(Target, (await SettingsRepository().GetAsync())!.LibraryFolderPath);
    }

    [Fact]
    public async Task ChangeAsync_Default_MovesTheLibraryBackAndRemovesTheSetting()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        await Changer(sameVolume: true).ChangeAsync(Target);

        var result = await Changer(sameVolume: true).ChangeAsync(folder: null);

        Assert.Equal(LibraryFolderChangeOutcome.Moved, result.Outcome);
        Assert.Equal(BoreaRoot, result.Folder);
        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.Null((await SettingsRepository().GetAsync())!.LibraryFolderPath);
    }

    [Fact]
    public async Task ChangeAsync_TargetHoldsALibraryAndTheCurrentOneNoInstance_UsesItWithoutAMove()
    {
        var instance = await SeedLibraryAsync(Target);
        var backup = Path.Combine(BoreaRoot, "Backups", Guid.NewGuid().ToString(), "saves", "old.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        await File.WriteAllTextAsync(backup, "old");

        var result = await Changer(sameVolume: true).ChangeAsync(Target);

        Assert.Equal(LibraryFolderChangeOutcome.Adopted, result.Outcome);
        await AssertLibraryAtAsync(Target, instance);
        Assert.True(File.Exists(backup));
        Assert.Equal(Target, (await SettingsRepository().GetAsync())!.LibraryFolderPath);
    }

    [Fact]
    public async Task ChangeAsync_BothFoldersHoldInstances_RefusesTheMerge()
    {
        var current = await SeedLibraryAsync(BoreaRoot);
        var other = await SeedLibraryAsync(Target);

        var result = await Changer(sameVolume: true).ChangeAsync(Target);

        Assert.Equal(LibraryFolderChangeOutcome.BothHaveInstances, result.Outcome);
        Assert.Contains(BoreaRoot, result.Message);
        Assert.Contains(Target, result.Message);
        await AssertLibraryAtAsync(BoreaRoot, current);
        await AssertLibraryAtAsync(Target, other);
        Assert.False(File.Exists(Path.Combine(BoreaRoot, "borea-settings.toml")));
    }

    [Fact]
    public async Task ChangeAsync_TargetHasBackupsButNoInstance_Refuses()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        Directory.CreateDirectory(Path.Combine(Target, "Backups", "stray"));

        var result = await Changer(sameVolume: true).ChangeAsync(Target);

        Assert.Equal(LibraryFolderChangeOutcome.TargetNotEmpty, result.Outcome);
        await AssertLibraryAtAsync(BoreaRoot, instance);
    }

    [Fact]
    public async Task ChangeAsync_LockedFile_RefusesAndMovesNothing()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var locked = ModFile(BoreaRoot, instance);

        LibraryFolderChangeResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await Changer(sameVolume: false).ChangeAsync(Target);

        Assert.Equal(LibraryFolderChangeOutcome.FileLocked, result.Outcome);
        Assert.Equal(locked, result.LockedFile);
        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public async Task ChangeAsync_GameRunsForAnInstance_Refuses()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        _launcher.Running.Add(instance.InstanceId);

        var result = await Changer(sameVolume: true).ChangeAsync(Target);

        Assert.Equal(LibraryFolderChangeOutcome.GameRunning, result.Outcome);
        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public async Task ChangeAsync_GameProcessRuns_Refuses()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        _gameProcessRunning = true;

        var result = await Changer(sameVolume: true).ChangeAsync(Target);

        Assert.Equal(LibraryFolderChangeOutcome.GameRunning, result.Outcome);
        await AssertLibraryAtAsync(BoreaRoot, instance);
    }

    [Fact]
    public async Task ChangeAsync_OtherBoreaRuns_RefusesAndMovesNothing()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        _boreaRunning = true;

        var result = await Changer(sameVolume: true).ChangeAsync(Target);

        Assert.Equal(LibraryFolderChangeOutcome.BoreaRunning, result.Outcome);
        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.False(Directory.Exists(Target));
        Assert.False(File.Exists(Path.Combine(BoreaRoot, "borea-settings.toml")));
    }

    [Fact]
    public async Task ChangeAsync_UpdateHoldsTheInstanceLock_Refuses()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var instances = new FileInstanceRepository(Paths());
        using var held = instances.TryHold([instance.InstanceId]);

        var result = await Changer(sameVolume: true, locks: instances).ChangeAsync(Target);

        Assert.NotNull(held);
        Assert.Equal(LibraryFolderChangeOutcome.InstanceBusy, result.Outcome);
        await AssertLibraryAtAsync(BoreaRoot, instance);
    }

    [Theory]
    [InlineData("relative", LibraryFolderChangeOutcome.NotAbsolute)]
    [InlineData("file", LibraryFolderChangeOutcome.IsFile)]
    [InlineData("current", LibraryFolderChangeOutcome.CurrentLibrary)]
    [InlineData("inside current", LibraryFolderChangeOutcome.InsideCurrentLibrary)]
    [InlineData("contains current", LibraryFolderChangeOutcome.ContainsCurrentLibrary)]
    [InlineData("game", LibraryFolderChangeOutcome.InsideGameDirectory)]
    [InlineData("game itself", LibraryFolderChangeOutcome.InsideGameDirectory)]
    [InlineData("profile", LibraryFolderChangeOutcome.InsideSharedProfile)]
    [InlineData("not writable", LibraryFolderChangeOutcome.NotWritable)]
    public async Task ChangeAsync_RefusedFolder_ChangesNothing(string place, LibraryFolderChangeOutcome expected)
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var game = Directory.CreateDirectory(Path.Combine(_tempRoot, "Game")).FullName;
        var file = Path.Combine(_tempRoot, "notes.txt");
        await File.WriteAllTextAsync(file, "notes");
        await SettingsRepository().SaveAsync(new BoreaSettings(game));
        var folder = place switch
        {
            "relative" => "Library",
            "file" => file,
            "current" => BoreaRoot,
            "inside current" => Path.Combine(BoreaRoot, "Library"),
            "contains current" => _tempRoot,
            "game" => Path.Combine(game, "Library"),
            "game itself" => game,
            "profile" => Path.Combine(SharedProfile, "Library"),
            _ => Path.Combine(file, "Library"),
        };
        var existed = Directory.Exists(folder);

        var result = await Changer(sameVolume: true).ChangeAsync(folder);

        Assert.Equal(expected, result.Outcome);
        Assert.False(result.Changed);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Null((await SettingsRepository().GetAsync())!.LibraryFolderPath);
        await AssertLibraryAtAsync(BoreaRoot, instance);
        if (!existed && Path.IsPathFullyQualified(folder))
            Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData(true, LibraryFolderChangeOutcome.InsideBoreaFolder)]
    [InlineData(false, LibraryFolderChangeOutcome.ContainsBoreaFolder)]
    public async Task ChangeAsync_CustomLibraryAndAFolderThatOverlapsBoreasOwnFolder_Refuses(bool inside, LibraryFolderChangeOutcome expected)
    {
        var instance = await SeedLibraryAsync(Target);
        await SettingsRepository().SaveAsync(new BoreaSettings(null, libraryFolderPath: Target));
        var ownFolder = Path.Combine(_tempRoot, "Local", "Borea");
        var folder = inside ? Path.Combine(ownFolder, "Library") : Path.GetDirectoryName(ownFolder)!;

        var result = await Changer(sameVolume: true, defaultFolder: ownFolder).ChangeAsync(folder);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(Target, (await SettingsRepository().GetAsync())!.LibraryFolderPath);
        await AssertLibraryAtAsync(Target, instance);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ChangeAsync_CurrentLibraryInOtherLetterCase_IsTheCurrentLibraryUnlessPathsAreCaseSensitive()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var folder = Path.Combine(_tempRoot, "BOREA");

        var result = await Changer(sameVolume: true).ChangeAsync(folder);

        Assert.Equal(OperatingSystem.IsLinux() ? LibraryFolderChangeOutcome.Moved : LibraryFolderChangeOutcome.CurrentLibrary, result.Outcome);
        await AssertLibraryAtAsync(OperatingSystem.IsLinux() ? folder : BoreaRoot, instance);
    }

    [UnixFact("Windows creates a symbolic link only with administrator rights or developer mode.")]
    public async Task ChangeAsync_FolderInsideTheCurrentLibraryThroughALink_Refuses()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var link = Path.Combine(_tempRoot, "Link");
        Directory.CreateSymbolicLink(link, BoreaRoot);

        var result = await Changer(sameVolume: true).ChangeAsync(Path.Combine(link, "Library"));

        Assert.Equal(LibraryFolderChangeOutcome.InsideCurrentLibrary, result.Outcome);
        await AssertLibraryAtAsync(BoreaRoot, instance);
        Assert.False(Directory.Exists(Path.Combine(BoreaRoot, "Library")));
    }

    [UnixFact("Windows needs an access rule to keep a folder from being written to.")]
    public async Task ChangeAsync_ExistingFolderBoreaCannotWriteTo_IsNotWritable()
    {
        var instance = await SeedLibraryAsync(BoreaRoot);
        var folder = Directory.CreateDirectory(Target).FullName;
        SetWritable(folder, writable: false);

        LibraryFolderChangeResult result;
        try
        {
            result = await Changer(sameVolume: true).ChangeAsync(folder);
        }
        finally
        {
            SetWritable(folder, writable: true);
        }

        Assert.Equal(LibraryFolderChangeOutcome.NotWritable, result.Outcome);
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        await AssertLibraryAtAsync(BoreaRoot, instance);
    }

    [Fact]
    public void OnSameVolume_TwoFoldersInTheTempFolder_IsTrue()
    {
        Assert.True(LibraryFolderChanger.OnSameVolume(Path.Combine(_tempRoot, "A"), Path.Combine(_tempRoot, "B")));
    }

    private LibraryFolderChanger Changer(bool sameVolume, IBoreaSettingsRepository? settings = null, IInstanceLocks? locks = null, string? defaultFolder = null, Action<string, string>? moveDirectory = null)
        => new(
            settings ?? SettingsRepository(),
            Paths(),
            defaultFolder ?? BoreaRoot,
            _launcher,
            locks ?? new FileInstanceRepository(Paths()),
            () => _gameProcessRunning,
            () => _boreaRunning,
            (_, _) => sameVolume,
            moveDirectory ?? Directory.Move);

    private GamePathProvider Paths(string? libraryFolder = null)
        => new(gameDirectory: null, boreaRoot: BoreaRoot, sharedProfileRoot: SharedProfile, libraryFolder: libraryFolder);

    private FileBoreaSettingsRepository SettingsRepository() => new(Paths());

    /// <summary>An instance with a mod file and a save backup, three files in all.</summary>
    private async Task<Instance> SeedLibraryAsync(string folder)
    {
        var instance = (await new FileInstanceRepository(Paths(folder)).CreateAsync("Career " + Guid.NewGuid().ToString("N")[..8], InstanceSource.Custom.Value)).Instance;

        var mod = ModFile(folder, instance);
        Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
        await File.WriteAllBytesAsync(mod, Enumerable.Range(0, 5000).Select(value => (byte)value).ToArray());
        File.SetLastWriteTimeUtc(mod, ModWrittenAt);

        var backup = BackupFile(folder, instance);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        await File.WriteAllTextAsync(backup, "orbit");
        return instance;
    }

    private static string ModFile(string folder, Instance instance)
        => Path.Combine(folder, "Instances", instance.InstanceId.ToString(), "mods", "Tools", "Tools.dll");

    private static string BackupFile(string folder, Instance instance)
        => Path.Combine(folder, "Backups", instance.InstanceId.ToString(), "saves", "Orbit", "save.bin");

    private static void SetWritable(string folder, bool writable)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute | (writable ? UnixFileMode.UserWrite : UnixFileMode.None));
    }

    private async Task AssertLibraryAtAsync(string folder, Instance instance)
    {
        var read = await new FileInstanceRepository(Paths(folder)).GetByIdAsync(instance.InstanceId);
        Assert.Equal(instance.Name, read?.Name);
        Assert.Equal(5000, new FileInfo(ModFile(folder, instance)).Length);
        Assert.Equal("orbit", await File.ReadAllTextAsync(BackupFile(folder, instance)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private sealed class FakeLauncher : ILauncher
    {
        public HashSet<Guid> Running { get; } = [];

        public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null) => throw new NotSupportedException();

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool IsRunning(Guid instanceId) => Running.Contains(instanceId);
    }

    private sealed class FailingSaveRepository(IBoreaSettingsRepository inner) : IBoreaSettingsRepository
    {
        public Task<BoreaSettings?> GetAsync(CancellationToken cancellationToken = default) => inner.GetAsync(cancellationToken);

        public Task SaveAsync(BoreaSettings settings, CancellationToken cancellationToken = default)
            => Task.FromException(new IOException("The disk is full."));
    }
}
