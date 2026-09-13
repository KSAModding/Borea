using System.ComponentModel;
using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Storage.Launch;
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

    /// <summary>Puts an empty file where the game's executable would be.</summary>
    private void PlaceGame()
    {
        Directory.CreateDirectory(GameDirectory);
        File.WriteAllBytes(Path.Combine(GameDirectory, "KSA.exe"), Array.Empty<byte>());
    }

    private SharedProfileLauncher Launcher(OsPlatform platform = OsPlatform.Windows) => new(_paths, _starter, platform);

    [Fact]
    public void Launch_Windows_StartsKsaExeInTheGameDirectoryWithNothingAdded()
    {
        PlaceGame();

        var result = Launcher().Launch();

        Assert.True(result.Started);
        Assert.Equal(SharedProfileLaunchOutcome.Started, result.Outcome);
        var process = Assert.Single(_starter.Processes);
        Assert.Equal(process.Id, result.ProcessId);

        var plan = Assert.Single(_starter.Plans);
        Assert.Same(plan, result.Plan);
        Assert.Equal(ExpectedExecutable, plan.Executable);
        Assert.Equal(Path.GetFullPath(GameDirectory), plan.WorkingDirectory);
        Assert.Empty(plan.Arguments);
        Assert.Empty(plan.EnvironmentVariables);
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

    [Theory]
    [InlineData(OsPlatform.Linux, "on Linux")]
    [InlineData(OsPlatform.MacOs, "on macOS")]
    [InlineData(null, "on this operating system")]
    public void Launch_PlatformWithoutAKnownExecutable_StartsNothing(OsPlatform? platform, string name)
    {
        PlaceGame();

        var result = new SharedProfileLauncher(_paths, _starter, platform).Launch();

        Assert.Equal(SharedProfileLaunchOutcome.UnknownExecutable, result.Outcome);
        Assert.Contains(name, result.Message);
        Assert.Null(result.Plan);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_UnknownExecutableAndNoGameDirectory_ReportsThePlatformFirst()
    {
        var launcher = new SharedProfileLauncher(new TestGamePathProvider(_tempRoot, hasGameDirectory: false), _starter, OsPlatform.Linux);

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
