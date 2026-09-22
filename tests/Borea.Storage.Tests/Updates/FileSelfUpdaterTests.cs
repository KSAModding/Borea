using Borea.Core.Mods;
using Borea.Core.Updates;
using Borea.Storage.Tests.Logging;
using Borea.Storage.Updates;

namespace Borea.Storage.Tests.Updates;

public sealed class FileSelfUpdaterTests : IDisposable
{
    private const string InstallFolderName = "Borea";

    private const string ArchiveFolderName = "Borea-0.2.0-win-x64";

    private const string ArchiveName = "Borea-0.2.0-win-x64.zip";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly RecordingLog _log = new();
    private readonly RecordingHandoverStarter _starter = new();
    private readonly FakeReleaseFiles _files = new();
    private readonly string _folder;
    private readonly string _programPath;
    private readonly string _replacedPath;

    public FileSelfUpdaterTests()
    {
        _folder = Path.Combine(_root, InstallFolderName);
        _programPath = Path.Combine(_folder, "borea.exe");
        _replacedPath = SelfUpdateCleanup.ReplacedPath(_programPath);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_programPath, "the running build");
        File.WriteAllText(Path.Combine(_folder, "LICENSE"), "the license of the running build");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private FileSelfUpdater Updater(string platform = "win-x64", BoreaProduct product = BoreaProduct.App, bool fromCommandLine = false)
        => new(_files, _log, product, _starter, _programPath, platform, isReleaseBuild: true, fromCommandLine);

    private static BoreaRelease Release(params string[] assetNames) =>
        new(ModVersion.Parse("0.2.0"), "v0.2.0", "https://github.com/KSAModding/Borea/releases/tag/v0.2.0")
        {
            Assets = assetNames.Select(name => new BoreaReleaseAsset(name, FakeReleaseFiles.Url(name))).ToList(),
        };

    /// <summary>Serves the zip of the App and a checksums file that records it.</summary>
    private BoreaRelease ServeWindowsRelease(byte[]? archive = null)
    {
        archive ??= ReleaseArchiveBuilder.Zip(ArchiveFolderName, "borea.exe", "the new build");
        _files.Add(ArchiveName, archive);
        _files.Add(BoreaArchive.ChecksumsFileName, ReleaseArchiveBuilder.Checksums((ArchiveName, archive)));
        return Release(ArchiveName, BoreaArchive.ChecksumsFileName);
    }

    /// <summary>The program of the build that waits in the staging folder, before it is put in place.</summary>
    private string StagedProgram()
    {
        var staging = Directory.GetDirectories(_folder, ".borea-update-*").Single();
        return Path.Combine(Directory.GetDirectories(staging).Single(), "borea.exe");
    }

    /// <summary>What the install folder holds, without the staging folder of an update that still runs.</summary>
    private string[] InstalledFiles() => Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public async Task StageAsync_ChecksTheArchiveAndLeavesTheRunningBuildAsItIs()
    {
        var release = ServeWindowsRelease();
        var phases = new List<SelfUpdatePhase>();

        var staged = await Updater().StageAsync(release, new SyncProgress(progress => phases.Add(progress.Phase)));

        Assert.Equal(_folder, staged.Folder);
        Assert.Equal(_programPath, staged.ProgramPath);
        Assert.Equal(_replacedPath, staged.ReplacedProgramPath);
        Assert.Equal("the new build", File.ReadAllText(StagedProgram()));
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal(["LICENSE", "borea.exe"], InstalledFiles());
        Assert.Equal([SelfUpdatePhase.Downloading, SelfUpdatePhase.Verifying, SelfUpdatePhase.Unpacking], phases.Distinct());
    }

    [Fact]
    public async Task Install_PutsTheNewBuildInTheFolderBoreaRunsIn()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());

        staged.Install();

        Assert.Equal(_programPath, staged.ProgramPath);
        Assert.Equal("the new build", File.ReadAllText(_programPath));
        Assert.Equal("the running build", File.ReadAllText(_replacedPath));
        Assert.Equal("MIT", File.ReadAllText(Path.Combine(_folder, "LICENSE")));
        Assert.Equal("Avalonia", File.ReadAllText(Path.Combine(_folder, "THIRD-PARTY-NOTICES.txt")));
        Assert.Equal([InstallFolderName], Directory.GetDirectories(_root).Select(Path.GetFileName));
        Assert.Empty(Directory.GetDirectories(_folder));
    }

    [Fact]
    public async Task Install_ATarArchive_IsUnpackedTheSameWay()
    {
        const string archiveName = "Borea-0.2.0-linux-x64.tar.gz";
        var archive = ReleaseArchiveBuilder.TarGz("Borea-0.2.0-linux-x64", "borea.exe", "the new build");
        _files.Add(archiveName, archive);
        _files.Add(BoreaArchive.ChecksumsFileName, ReleaseArchiveBuilder.Checksums((archiveName, archive)));

        var staged = await Updater("linux-x64").StageAsync(Release(archiveName, BoreaArchive.ChecksumsFileName));
        staged.Install();

        Assert.Equal(_programPath, staged.ProgramPath);
        Assert.Equal("the new build", File.ReadAllText(_programPath));
        Assert.Equal("the running build", File.ReadAllText(_replacedPath));
    }

    [Fact]
    public async Task Install_ALeftoverOfAnEarlierUpdate_MakesRoomForTheReplacedBuild()
    {
        File.WriteAllText(_replacedPath, "a build of last year");
        var staged = await Updater().StageAsync(ServeWindowsRelease());

        staged.Install();

        Assert.Equal("the running build", File.ReadAllText(_replacedPath));
    }

    [Fact]
    public async Task Install_ANewBuildThatChangedAfterTheCheck_LeavesTheRunningBuildInPlace()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());
        File.WriteAllText(StagedProgram(), "somebody else's build");

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.Install);

        Assert.Equal(SelfUpdateFailure.Install, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.False(File.Exists(_replacedPath));
        Assert.Empty(Directory.GetDirectories(_folder));
    }

    [Fact]
    public async Task Install_AFileThatCannotTakeItsPlace_PutsTheFilesOfTheRunningBuildBack()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());

        // A folder where a file of the new build has to go stops the move, and nothing else does.
        Directory.CreateDirectory(Path.Combine(_folder, "THIRD-PARTY-NOTICES.txt"));

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.Install);

        Assert.Equal(SelfUpdateFailure.Install, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal("the license of the running build", File.ReadAllText(Path.Combine(_folder, "LICENSE")));
        Assert.False(File.Exists(_replacedPath));
        Assert.Equal(["THIRD-PARTY-NOTICES.txt"], Directory.GetDirectories(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Install_ALeftoverThatCannotBeRemoved_PutsTheFilesOfTheRunningBuildBack()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());

        // A folder with the name of the replaced program cannot be deleted to make room for it.
        Directory.CreateDirectory(_replacedPath);

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.Install);

        Assert.Equal(SelfUpdateFailure.Install, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal("the license of the running build", File.ReadAllText(Path.Combine(_folder, "LICENSE")));
        Assert.False(File.Exists(Path.Combine(_folder, "THIRD-PARTY-NOTICES.txt")));
    }

    [Fact]
    public async Task Install_WhileAnotherUpdateHoldsTheFolder_ChangesNothing()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());

        using var held = SelfUpdateLock.TryTake(_folder);
        Assert.NotNull(held);
        var failure = Assert.Throws<SelfUpdateFailedException>(staged.Install);

        Assert.Equal(SelfUpdateFailure.Install, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal("the license of the running build", File.ReadAllText(Path.Combine(_folder, "LICENSE")));
        Assert.False(File.Exists(_replacedPath));
    }

    [Fact]
    public async Task Install_LeavesTheFolderFreeForTheNextUpdate()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());
        staged.Install();

        using var held = SelfUpdateLock.TryTake(_folder);

        Assert.NotNull(held);
    }

    [Fact]
    public async Task HandOver_StartsTheNewBuildFromTheSamePathWithTheReplacedOneAndAReceipt()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());
        staged.Install();

        staged.HandOver();

        Assert.Equal(_programPath, _starter.ProgramPath);
        var arguments = _starter.Arguments.ToArray();
        var handover = SelfUpdateHandover.Take(ref arguments);
        Assert.NotNull(handover);
        Assert.Equal(_replacedPath, handover!.PreviousProgramPath);
        Assert.Equal(Environment.ProcessId, handover.PreviousProcessId);
        Assert.False(handover.FromCommandLine);
        Assert.True(SelfUpdateReceipt.Matches(_folder, handover, _replacedPath));
    }

    [Fact]
    public async Task HandOver_OfACommand_TellsTheNewBuildToOpenNoWindow()
    {
        var staged = await Updater(fromCommandLine: true).StageAsync(ServeWindowsRelease());
        staged.Install();

        staged.HandOver();

        var arguments = _starter.Arguments.ToArray();
        Assert.True(SelfUpdateHandover.Take(ref arguments)?.FromCommandLine);
    }

    [Fact]
    public async Task HandOver_ANewBuildThatChangedAfterItWasPutInPlace_IsNotStartedAndTheReplacedBuildComesBack()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());
        staged.Install();
        File.WriteAllText(_programPath, "somebody else's build");

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.HandOver);

        Assert.Equal(SelfUpdateFailure.Start, failure.Reason);
        Assert.Null(_starter.ProgramPath);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.False(File.Exists(_replacedPath));
    }

    [Fact]
    public async Task HandOver_ANewBuildThatDoesNotStart_BringsTheReplacedBuildBack()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());
        staged.Install();
        _starter.Failure = new InvalidOperationException("no process was started");

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.HandOver);

        Assert.Equal(SelfUpdateFailure.Start, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.False(File.Exists(_replacedPath));
        Assert.False(File.Exists(Path.Combine(_folder, SelfUpdateReceipt.FileName)));
    }

    [Fact]
    public async Task HandOver_AProgramPathThatTakesNoFileBack_SaysThatBoreaNeedsAHandRename()
    {
        var staged = await Updater().StageAsync(ServeWindowsRelease());
        staged.Install();

        // A folder at the path of the program takes neither a hash nor the replaced build back.
        File.Delete(_programPath);
        Directory.CreateDirectory(_programPath);

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.HandOver);

        Assert.Equal(SelfUpdateFailure.Restore, failure.Reason);
        Assert.Null(_starter.ProgramPath);
        Assert.Equal("the running build", File.ReadAllText(_replacedPath));
    }

    [Fact]
    public async Task StageAsync_AFolderOfAnUpdateThatDidNotFinish_IsSweptBeforeTheNextOne()
    {
        var old = Path.Combine(_folder, SelfUpdateCleanup.StagingPrefix + "olderthanthis");
        Directory.CreateDirectory(Path.Combine(old, "Borea-0.1.9-win-x64"));
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow - SelfUpdateCleanup.StagingAge - TimeSpan.FromMinutes(1));

        var staged = await Updater().StageAsync(ServeWindowsRelease());
        staged.Install();

        Assert.False(Directory.Exists(old));
        Assert.Empty(Directory.GetDirectories(_folder));
    }

    [Fact]
    public async Task StageAsync_AFolderOfAnUpdateThatRuns_IsLeftAlone()
    {
        var other = Path.Combine(_folder, SelfUpdateCleanup.StagingPrefix + "anotherupdate");
        Directory.CreateDirectory(other);

        var staged = await Updater().StageAsync(ServeWindowsRelease());
        staged.Install();

        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public async Task StageAsync_AChecksumsFileThatDoesNotArrive_IsAFailedDownload()
    {
        var archive = ReleaseArchiveBuilder.Zip(ArchiveFolderName, "borea.exe", "the new build");
        _files.Add(ArchiveName, archive);

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(
            () => Updater().StageAsync(Release(ArchiveName, BoreaArchive.ChecksumsFileName)));

        Assert.Equal(SelfUpdateFailure.Download, failure.Reason);
        Assert.Empty(Directory.GetDirectories(_folder));
    }

    [Fact]
    public async Task StageAsync_OtherBytesThanTheChecksums_StopsAndLeavesNothingBehind()
    {
        var release = ServeWindowsRelease();
        _files.Add(ArchiveName, ReleaseArchiveBuilder.Zip(ArchiveFolderName, "borea.exe", "other bytes"));

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater().StageAsync(release));

        Assert.Equal(SelfUpdateFailure.Checksum, failure.Reason);
        Assert.Empty(Directory.GetDirectories(_folder));
        Assert.Equal("the running build", File.ReadAllText(_programPath));
    }

    [Fact]
    public async Task StageAsync_NoChecksumsFile_StopsBeforeItDownloadsAnything()
    {
        var archive = ReleaseArchiveBuilder.Zip(ArchiveFolderName, "borea.exe", "the new build");
        _files.Add(ArchiveName, archive);

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater().StageAsync(Release(ArchiveName)));

        Assert.Equal(SelfUpdateFailure.Checksum, failure.Reason);
        Assert.Empty(_files.Requested);
    }

    [Fact]
    public async Task StageAsync_NoArchiveForThisBuild_Stops()
    {
        var release = ServeWindowsRelease();

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater(product: BoreaProduct.Cli).StageAsync(release));

        Assert.Equal(SelfUpdateFailure.NoArchive, failure.Reason);
    }

    [Fact]
    public async Task StageAsync_AFailedDownload_StopsAndLeavesNothingBehind()
    {
        var release = ServeWindowsRelease();
        _files.DownloadFailure = new HttpRequestException("the host did not answer");

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater().StageAsync(release));

        Assert.Equal(SelfUpdateFailure.Download, failure.Reason);
        Assert.Empty(Directory.GetDirectories(_folder));
    }

    [Fact]
    public async Task StageAsync_AnArchiveWithoutTheProgram_Stops()
    {
        var archive = ReleaseArchiveBuilder.Zip(ArchiveFolderName, "something-else", "not the program");
        _files.Add(ArchiveName, archive);
        _files.Add(BoreaArchive.ChecksumsFileName, ReleaseArchiveBuilder.Checksums((ArchiveName, archive)));

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(
            () => Updater().StageAsync(Release(ArchiveName, BoreaArchive.ChecksumsFileName)));

        Assert.Equal(SelfUpdateFailure.Unpack, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Empty(Directory.GetDirectories(_folder));
    }

    [Fact]
    public async Task StageAsync_AnArchiveWithAFolderInTheBuild_Stops()
    {
        var archive = ReleaseArchiveBuilder.Zip(ArchiveFolderName, "borea.exe", "the new build", extraPath: "plugins/extra.txt");
        _files.Add(ArchiveName, archive);
        _files.Add(BoreaArchive.ChecksumsFileName, ReleaseArchiveBuilder.Checksums((ArchiveName, archive)));

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(
            () => Updater().StageAsync(Release(ArchiveName, BoreaArchive.ChecksumsFileName)));

        Assert.Equal(SelfUpdateFailure.Unpack, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Empty(Directory.GetDirectories(_folder));
    }

    [Fact]
    public void GetReadiness_APlainFolderThisUserOwns_AllowsTheUpdate()
        => Assert.Equal(SelfUpdateReadiness.Ready, Updater().GetReadiness());

    [Fact]
    public void GetReadiness_APackageMarker_NamesTheManagerAndStopsTheUpdate()
    {
        File.WriteAllText(Path.Combine(_folder, FileSelfUpdater.PackageMarkerFileName), "winget\nanything else\n");

        var readiness = Updater().GetReadiness();

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.PackageManaged, "winget"), readiness);
        Assert.False(readiness.CanUpdate);
    }

    [Fact]
    public void GetReadiness_AnEmptyPackageMarker_StopsTheUpdateWithoutAName()
    {
        File.WriteAllText(Path.Combine(_folder, FileSelfUpdater.PackageMarkerFileName), string.Empty);

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.PackageManaged), Updater().GetReadiness());
    }

    [Fact]
    public async Task StageAsync_APackageMarker_StopsBeforeAnythingIsFetched()
    {
        var release = ServeWindowsRelease();
        File.WriteAllText(Path.Combine(_folder, FileSelfUpdater.PackageMarkerFileName), "pacman");

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater().StageAsync(release));

        Assert.Equal(SelfUpdateFailure.Blocked, failure.Reason);
        Assert.Empty(_files.Requested);
    }

    [Fact]
    public void GetReadiness_ABuildThatIsNoReleaseArchive_StopsTheUpdate()
    {
        var updater = new FileSelfUpdater(_files, _log, BoreaProduct.App, _starter, _programPath, "win-x64", isReleaseBuild: false);

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.NotAReleaseBuild), updater.GetReadiness());
    }

    [Fact]
    public void GetReadiness_AFolderThatTakesNoFile_StopsTheUpdate()
    {
        var gone = Path.Combine(_root, "not-there", "borea.exe");
        var updater = new FileSelfUpdater(_files, _log, BoreaProduct.App, _starter, gone, "win-x64", isReleaseBuild: true);

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.ReadOnlyLocation), updater.GetReadiness());
    }

    [Fact]
    public void GetReadiness_APlatformWithoutRelease_StopsTheUpdate()
    {
        var updater = new FileSelfUpdater(_files, _log, BoreaProduct.App, _starter, _programPath, platform: null, isReleaseBuild: true);

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.UnsupportedPlatform), updater.GetReadiness());
    }

    /// <summary>Reports on the calling thread, so the test reads every step in order.</summary>
    private sealed class SyncProgress(Action<SelfUpdateProgress> report) : IProgress<SelfUpdateProgress>
    {
        public void Report(SelfUpdateProgress value) => report(value);
    }
}
