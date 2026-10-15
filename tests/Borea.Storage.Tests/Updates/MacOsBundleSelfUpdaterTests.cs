using Borea.Core.Mods;
using Borea.Core.Updates;
using Borea.Storage.Tests.Logging;
using Borea.Storage.Updates;

namespace Borea.Storage.Tests.Updates;

public sealed class MacOsBundleSelfUpdaterTests : IDisposable
{
    private const string BundleName = "Borea.app";

    private const string ArchiveName = "Borea-0.2.0-macos-arm64.tar.gz";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly RecordingLog _log = new();
    private readonly RecordingHandoverStarter _starter = new();
    private readonly FakeReleaseFiles _files = new();
    private readonly string _folder;
    private readonly string _bundlePath;
    private readonly string _programPath;
    private readonly string _replacedPath;

    public MacOsBundleSelfUpdaterTests()
    {
        _folder = Path.Combine(_root, "Applications");
        _bundlePath = Path.Combine(_folder, BundleName);
        _programPath = Path.Combine(_bundlePath, "Contents", "MacOS", "borea");
        _replacedPath = SelfUpdateCleanup.ReplacedPath(_bundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_programPath)!);
        File.WriteAllText(_programPath, "the running build");
        File.WriteAllText(Path.Combine(_bundlePath, "Contents", "Info.plist"), "the plist of the running build");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MacOsBundleSelfUpdater Updater(string? platform = "macos-arm64", bool fromCommandLine = false, string? programPath = null)
        => new(_files, _log, _starter, programPath ?? _programPath, platform, isReleaseBuild: true, fromCommandLine);

    private static BoreaRelease Release(params string[] assetNames) =>
        new(ModVersion.Parse("0.2.0"), "v0.2.0", "https://github.com/KSAModding/Borea/releases/tag/v0.2.0")
        {
            Assets = assetNames.Select(name => new BoreaReleaseAsset(name, FakeReleaseFiles.Url(name))).ToList(),
        };

    /// <summary>Serves the macOS App archive and a checksums file that records it.</summary>
    private BoreaRelease ServeMacRelease(byte[]? archive = null)
    {
        archive ??= ReleaseArchiveBuilder.BundleTarGz(BundleName, "the new build");
        _files.Add(ArchiveName, archive);
        _files.Add(BoreaArchive.ChecksumsFileName, ReleaseArchiveBuilder.Checksums((ArchiveName, archive)));
        return Release(ArchiveName, BoreaArchive.ChecksumsFileName);
    }

    /// <summary>The program of the bundle that waits in the staging folder, before it is put in place.</summary>
    private string StagedProgram()
    {
        var staging = Directory.GetDirectories(_folder, ".borea-update-*").Single();
        return Path.Combine(Directory.GetDirectories(staging).Single(), "Contents", "MacOS", "borea");
    }

    /// <summary>The program inside the bundle that was moved aside.</summary>
    private string ReplacedProgram() => Path.Combine(_replacedPath, "Contents", "MacOS", "borea");

    /// <summary>What the folder holds, without the staging folder of an update that still runs.</summary>
    private string[] Bundles() => Directory.GetDirectories(_folder).Select(Path.GetFileName).Where(name => !name!.StartsWith('.')).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public async Task StageAsync_ChecksTheArchiveAndLeavesTheInstalledBundleAsItIs()
    {
        var release = ServeMacRelease();
        var phases = new List<SelfUpdatePhase>();

        var staged = await Updater().StageAsync(release, new SyncProgress(progress => phases.Add(progress.Phase)));

        Assert.Equal(_folder, staged.Folder);
        Assert.Equal(_bundlePath, staged.ProgramPath);
        Assert.Equal(_replacedPath, staged.ReplacedProgramPath);
        Assert.Equal("the new build", File.ReadAllText(StagedProgram()));
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal([BundleName], Bundles());
        Assert.Equal([SelfUpdatePhase.Downloading, SelfUpdatePhase.Verifying, SelfUpdatePhase.Unpacking], phases.Distinct());
    }

    [Fact]
    public async Task Install_PutsTheNewBundleInThePlaceOfTheInstalledOne()
    {
        var staged = await Updater().StageAsync(ServeMacRelease());

        staged.Install();

        Assert.Equal("the new build", File.ReadAllText(_programPath));
        Assert.Equal("the plist of the new build", File.ReadAllText(Path.Combine(_bundlePath, "Contents", "Info.plist")));
        Assert.Equal("MIT", File.ReadAllText(Path.Combine(_bundlePath, "Contents", "MacOS", "LICENSE")));
        Assert.Equal("the running build", File.ReadAllText(ReplacedProgram()));
        Assert.Equal([BundleName, BundleName + ".old"], Bundles());
        Assert.DoesNotContain(Directory.GetFileSystemEntries(_bundlePath, "*", SearchOption.AllDirectories), entry => Path.GetFileName(entry).StartsWith(".borea", StringComparison.Ordinal));

        // the empty staging folder goes once the new build started
        Assert.Single(Directory.GetDirectories(_folder, ".borea-update-*"));
        staged.HandOver();
        Assert.Empty(Directory.GetDirectories(_folder, ".borea-update-*"));
    }

    [Fact]
    public async Task Install_ALeftoverOfAnEarlierUpdate_MakesRoomForTheReplacedBundle()
    {
        Directory.CreateDirectory(Path.Combine(_replacedPath, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(_replacedPath, "Contents", "MacOS", "borea"), "a build of last year");
        var staged = await Updater().StageAsync(ServeMacRelease());

        staged.Install();

        Assert.Equal("the running build", File.ReadAllText(ReplacedProgram()));
    }

    [Fact]
    public async Task Install_ANewBuildThatChangedAfterTheCheck_LeavesTheInstalledBundleInPlace()
    {
        var staged = await Updater().StageAsync(ServeMacRelease());
        File.WriteAllText(StagedProgram(), "somebody else's build");

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.Install);

        Assert.Equal(SelfUpdateFailure.Install, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal([BundleName], Bundles());
        Assert.Empty(Directory.GetDirectories(_folder, ".borea-update-*"));
    }

    [Fact]
    public async Task Install_WhileAnotherUpdateHoldsTheFolder_ChangesNothing()
    {
        var staged = await Updater().StageAsync(ServeMacRelease());

        using var held = SelfUpdateLock.TryTake(_folder);
        Assert.NotNull(held);
        var failure = Assert.Throws<SelfUpdateFailedException>(staged.Install);

        Assert.Equal(SelfUpdateFailure.Install, failure.Reason);
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal([BundleName], Bundles());
    }

    [Fact]
    public async Task HandOver_StartsTheProgramInsideTheNewBundleWithTheReplacedBundleAndAReceipt()
    {
        var staged = await Updater().StageAsync(ServeMacRelease());
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
        var staged = await Updater(fromCommandLine: true).StageAsync(ServeMacRelease());
        staged.Install();

        staged.HandOver();

        var arguments = _starter.Arguments.ToArray();
        Assert.True(SelfUpdateHandover.Take(ref arguments)!.FromCommandLine);
    }

    [Fact]
    public async Task HandOver_ANewBuildThatChangedAfterItWasPutInPlace_IsNotStartedAndTheReplacedBundleComesBack()
    {
        var staged = await Updater().StageAsync(ServeMacRelease());
        staged.Install();
        File.WriteAllText(_programPath, "somebody else's build");

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.HandOver);

        Assert.Equal(SelfUpdateFailure.Start, failure.Reason);
        Assert.Null(_starter.ProgramPath);
        AssertTheInstalledBundleIsBack();
    }

    [Fact]
    public async Task HandOver_ANewBuildThatDoesNotStart_BringsTheReplacedBundleBack()
    {
        _starter.Failure = new InvalidOperationException("No process was started.");
        var staged = await Updater().StageAsync(ServeMacRelease());
        staged.Install();

        var failure = Assert.Throws<SelfUpdateFailedException>(staged.HandOver);

        Assert.Equal(SelfUpdateFailure.Start, failure.Reason);
        AssertTheInstalledBundleIsBack();
    }

    private void AssertTheInstalledBundleIsBack()
    {
        Assert.Equal("the running build", File.ReadAllText(_programPath));
        Assert.Equal("the plist of the running build", File.ReadAllText(Path.Combine(_bundlePath, "Contents", "Info.plist")));
        Assert.Equal([BundleName], Bundles());
        Assert.False(File.Exists(Path.Combine(_folder, SelfUpdateReceipt.FileName)));
        Assert.Empty(Directory.GetDirectories(_folder, ".borea-update-*"));
    }

    [Fact]
    public async Task StageAsync_AnArchiveWithoutABundle_Stops()
    {
        var release = ServeMacRelease(ReleaseArchiveBuilder.TarGz("Borea", "borea", "the new build"));

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater().StageAsync(release));

        Assert.Equal(SelfUpdateFailure.Unpack, failure.Reason);
        Assert.Equal([BundleName], Directory.GetDirectories(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task StageAsync_ABundleWithoutTheProgram_Stops()
    {
        var release = ServeMacRelease(ReleaseArchiveBuilder.BundleTarGz(BundleName, "the new build", programName: "something-else"));

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater().StageAsync(release));

        Assert.Equal(SelfUpdateFailure.Unpack, failure.Reason);
        Assert.Contains("borea", failure.Message, StringComparison.Ordinal);
        Assert.Equal([BundleName], Directory.GetDirectories(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task StageAsync_NoAppArchiveForThisPlatform_Stops()
    {
        var release = Release("Borea-Cli-0.2.0-macos-arm64.tar.gz", BoreaArchive.ChecksumsFileName);

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => Updater().StageAsync(release));

        Assert.Equal(SelfUpdateFailure.NoArchive, failure.Reason);
        Assert.Empty(_files.Requested);
    }

    [Fact]
    public void GetReadiness_ABundleInAFolderThisUserOwns_AllowsTheUpdate()
    {
        Assert.Equal(SelfUpdateReadiness.Ready, Updater().GetReadiness());
    }

    [Fact]
    public void GetReadiness_APackageMarkerBesideTheProgram_NamesTheManagerAndStopsTheUpdate()
    {
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_programPath)!, FileSelfUpdater.PackageMarkerFileName), "brew\n");

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.PackageManaged, "brew"), Updater().GetReadiness());
    }

    [Fact]
    public void GetReadiness_AProgramOutsideABundle_StopsTheUpdate()
    {
        var updater = Updater(programPath: Path.Combine(_root, "Borea", "borea"));

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.NotAReleaseBuild), updater.GetReadiness());
    }

    [Fact]
    public void GetReadiness_ABuildThatIsNoReleaseArchive_StopsTheUpdate()
    {
        var updater = new MacOsBundleSelfUpdater(_files, _log, _starter, _programPath, "macos-arm64", isReleaseBuild: false);

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.NotAReleaseBuild), updater.GetReadiness());
    }

    [Fact]
    public void GetReadiness_AFolderThatTakesNoFile_StopsTheUpdate()
    {
        var gone = Path.Combine(_root, "not-there", "Borea.app", "Contents", "MacOS", "borea");

        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.ReadOnlyLocation), Updater(programPath: gone).GetReadiness());
    }

    [Fact]
    public void GetReadiness_APlatformWithoutRelease_StopsTheUpdate()
    {
        Assert.Equal(new SelfUpdateReadiness(SelfUpdateBlock.UnsupportedPlatform), Updater(platform: null).GetReadiness());
    }

    private sealed class SyncProgress(Action<SelfUpdateProgress> report) : IProgress<SelfUpdateProgress>
    {
        public void Report(SelfUpdateProgress value) => report(value);
    }
}
