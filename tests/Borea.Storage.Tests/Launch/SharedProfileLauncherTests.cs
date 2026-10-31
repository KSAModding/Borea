using System.ComponentModel;
using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Storage.Launch;
using Borea.Storage.Tests.Game;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Launch;

public sealed class SharedProfileLauncherTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FakeProcessStarter _starter = new();

    public SharedProfileLauncherTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
    }

    private string GameDirectory => Path.Combine(_tempRoot, "Game");

    private string ExpectedExecutable => Path.Combine(Path.GetFullPath(GameDirectory), "KSA.exe");

    private void PlaceGame()
    {
        Directory.CreateDirectory(GameDirectory);
        File.WriteAllBytes(Path.Combine(GameDirectory, "KSA.exe"), Array.Empty<byte>());
    }

    private SharedProfileLauncher Launcher(OsPlatform platform = OsPlatform.Windows) => new(_paths, _starter, platform);

    [Theory]
    [InlineData(OsPlatform.Windows, "KSA.exe")]
    [InlineData(OsPlatform.Linux, "KSA")]
    public void Launch_BuildOfTheSystem_StartsItsExecutableInTheGameDirectoryWithNothingAdded(OsPlatform platform, string executable)
    {
        Directory.CreateDirectory(GameDirectory);
        File.WriteAllBytes(Path.Combine(GameDirectory, executable), Array.Empty<byte>());

        var result = Launcher(platform).Launch();

        Assert.True(result.Started);
        Assert.Equal(SharedProfileLaunchOutcome.Started, result.Outcome);
        var process = Assert.Single(_starter.Processes);
        Assert.Equal(process.Id, result.ProcessId);

        var plan = Assert.Single(_starter.Plans);
        Assert.Same(plan, result.Plan);
        Assert.Equal(Path.Combine(Path.GetFullPath(GameDirectory), executable), plan.Executable);
        Assert.Equal(Path.GetFullPath(GameDirectory), plan.WorkingDirectory);
        Assert.Empty(plan.Arguments);
        Assert.Empty(plan.EnvironmentVariables);
    }

    [Fact]
    public void Launch_WithArguments_PassesEachAsItIs()
    {
        PlaceGame();

        Launcher().Launch(["-windowed", "a b"]);

        Assert.Equal(new[] { "-windowed", "a b" }, Assert.Single(_starter.Plans).Arguments);
    }

    [Fact]
    public void Launch_Started_ReleasesTheHandleAtOnce()
    {
        PlaceGame();

        Launcher().Launch();

        Assert.True(Assert.Single(_starter.Processes).Disposed);
    }

    [Fact]
    public void Launch_NoGameDirectoryConfigured_ReportsIt()
    {
        var launcher = new SharedProfileLauncher(new TestGamePathProvider(_tempRoot, hasGameDirectory: false), _starter, OsPlatform.Windows);

        var result = launcher.Launch();

        Assert.Equal(SharedProfileLaunchOutcome.NoGameDirectory, result.Outcome);
        Assert.Contains("game directory", result.Message);
        Assert.Null(result.Plan);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_WindowsBuildInAWinePrefixOnMacOs_StartsNothing()
    {
        WineFixtures.Prefix(_tempRoot);
        PlaceGame();

        var result = Launcher(OsPlatform.MacOs).Launch();

        Assert.Equal(SharedProfileLaunchOutcome.WindowsBuild, result.Outcome);
        Assert.Contains("Wine prefix", result.Message);
        Assert.Equal(Path.GetFileName(_tempRoot), Path.GetFileName(result.Wine?.PrefixRoot));
        Assert.Empty(_starter.Plans);
    }

    private string BundlePath => Path.Combine(Path.GetFullPath(_tempRoot), "Kitten Space Agency.app");

    /// <summary>A launcher on macOS whose probe finds a wrapper app around the temp root, with the drives it is given.</summary>
    private SharedProfileLauncher WrapperLauncher(Dictionary<char, string> drives)
    {
        var wrapper = new WineWrapper(BundlePath, Path.Combine(BundlePath, "Contents", "MacOS", "launcher"));
        var install = new WineInstall(Path.GetFullPath(_tempRoot), WineFixtures.Drives(drives), wrapper);
        return new SharedProfileLauncher(_paths, _starter, OsPlatform.MacOs, new FakeWineProbe(install));
    }

    private Dictionary<char, string> HostRootAsDriveZ() => new() { ['z'] = Path.GetPathRoot(Path.GetFullPath(_tempRoot))! };

    [Fact]
    public void Launch_WindowsBuildInAWrapperOnMacOs_StartsKsaExeThroughTheWrapper()
    {
        PlaceGame();

        var result = WrapperLauncher(HostRootAsDriveZ()).Launch();

        Assert.True(result.Started);
        var plan = Assert.Single(_starter.Plans);
        Assert.Equal(Path.Combine(BundlePath, "Contents", "MacOS", "launcher"), plan.Executable);
        Assert.Equal([ExpectedExecutable], plan.Arguments);
        Assert.Equal(Path.GetFullPath(GameDirectory), plan.WorkingDirectory);
        Assert.Empty(plan.EnvironmentVariables);
    }

    [Fact]
    public void Launch_WrapperWithArguments_StartsNothing()
    {
        PlaceGame();

        var result = WrapperLauncher(HostRootAsDriveZ()).Launch(["-windowed"]);

        Assert.Equal(SharedProfileLaunchOutcome.WrapperArguments, result.Outcome);
        Assert.Contains("Kitten Space Agency.app", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_WrapperAndTheGameOnNoDrive_StartsNothingAndNamesTheExecutable()
    {
        PlaceGame();

        var result = WrapperLauncher(new Dictionary<char, string> { ['c'] = Directory.CreateDirectory(Path.Combine(_tempRoot, "drive_c")).FullName }).Launch();

        Assert.Equal(SharedProfileLaunchOutcome.PathOutsidePrefix, result.Outcome);
        Assert.Equal(ExpectedExecutable, result.UnmappedPath);
        Assert.Contains($"'{ExpectedExecutable}'", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_WindowsBuildWithoutAPrefixOnLinux_StartsNothing()
    {
        PlaceGame();

        var result = Launcher(OsPlatform.Linux).Launch();

        Assert.Equal(SharedProfileLaunchOutcome.WindowsBuild, result.Outcome);
        Assert.Contains("found no Wine prefix", result.Message);
        Assert.Null(result.Wine);
        Assert.Empty(_starter.Plans);
    }

    [Theory]
    [InlineData(OsPlatform.MacOs, "on macOS")]
    [InlineData(null, "on this operating system")]
    public void Launch_PlatformWithoutAKnownExecutable_StartsNothing(OsPlatform? platform, string name)
    {
        Directory.CreateDirectory(GameDirectory);

        var result = new SharedProfileLauncher(_paths, _starter, platform).Launch();

        Assert.Equal(SharedProfileLaunchOutcome.UnknownExecutable, result.Outcome);
        Assert.Contains(name, result.Message);
        Assert.Null(result.Plan);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_UnknownExecutableAndNoGameDirectory_ReportsThePlatformFirst()
    {
        var launcher = new SharedProfileLauncher(new TestGamePathProvider(_tempRoot, hasGameDirectory: false), _starter, OsPlatform.MacOs);

        var result = launcher.Launch();

        Assert.Equal(SharedProfileLaunchOutcome.UnknownExecutable, result.Outcome);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_ExecutableMissing_ReportsThePath()
    {
        Directory.CreateDirectory(GameDirectory);

        var result = Launcher().Launch();

        Assert.Equal(SharedProfileLaunchOutcome.ExecutableMissing, result.Outcome);
        Assert.Contains(ExpectedExecutable, result.Message);
        Assert.Equal(ExpectedExecutable, result.Plan!.Executable);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_SystemRefusesTheStart_ReportsIt()
    {
        PlaceGame();
        _starter.Failure = new Win32Exception(5, "Access is denied.");

        var result = Launcher().Launch();

        Assert.Equal(SharedProfileLaunchOutcome.StartFailed, result.Outcome);
        Assert.Contains("Access is denied.", result.Message);
        Assert.Equal(ExpectedExecutable, result.Plan!.Executable);
        Assert.Null(result.ProcessId);
        Assert.Empty(_starter.Processes);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new SharedProfileLauncher(null!, _starter));
        Assert.Throws<ArgumentNullException>(() => new SharedProfileLauncher(_paths, null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
