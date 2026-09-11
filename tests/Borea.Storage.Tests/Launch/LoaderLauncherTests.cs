using System.ComponentModel;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;
using Borea.Storage.Paths;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Launch;

public sealed class LoaderLauncherTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FakeProcessStarter _starter = new();
    private readonly LoaderLauncher _launcher;
    private readonly Instance _instance = new("Test", InstanceSource.Custom.Value);

    public LoaderLauncherTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _launcher = new LoaderLauncher(_paths, _starter);
    }

    private string StarMapDirectory => Path.Combine(_tempRoot, "StarMap");

    /// <summary>Puts an empty file where the loader's executable would be.</summary>
    private string PlaceStarMap(string launch = "StarMap.exe")
    {
        var executable = Path.Combine(StarMapDirectory, launch.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, Array.Empty<byte>());
        return executable;
    }

    private static ModMetadata LoaderListing(string modId = "StarMap", LoaderProvides? provides = null, bool standalone = true) => new(
        specVersion: 1,
        modId: modId,
        source: "TestSource",
        name: "StarMap",
        authors: new[] { "KlaasWhite" },
        abstractText: "The loader.",
        license: "MIT",
        links: MetadataFixtures.SampleLinks(),
        gameMin: "2026.8.3.5117",
        type: ContentType.ModLoader,
        install: standalone ? new InstallDescriptor(target: InstallAnchor.Standalone) : null,
        provides: provides);

    private static LoaderProvides StarMapProvides(string launch = "StarMap.exe") => new(
        launch: launch,
        contentDir: InstallAnchor.Mods,
        configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation"));

    [Fact]
    public void Launch_KnownLoader_StartsItInItsDirectoryWithTheInstanceRoot()
    {
        var executable = PlaceStarMap();
        var instanceRoot = Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.True(result.Started);
        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.Equal(Assert.Single(_starter.Processes).Id, result.ProcessId);
        Assert.Contains("StarMap", result.Message);

        var plan = Assert.Single(_starter.Plans);
        Assert.Same(plan, result.Plan);
        Assert.Equal(executable, plan.Executable);
        Assert.Equal(new[] { "-InstancePath", instanceRoot }, plan.Arguments);
        Assert.Equal(instanceRoot, plan.EnvironmentVariables["STARMAP_INSTANCE_PATH"]);
        Assert.Equal(Path.GetFullPath(StarMapDirectory), plan.WorkingDirectory);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_NestedLaunchTarget_ResolvesUnderTheLoaderDirectory()
    {
        var executable = PlaceStarMap("bin/StarMap.exe");

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides("bin/StarMap.exe")));

        Assert.True(result.Started);
        Assert.Equal(executable, result.Plan!.Executable);
    }

    [Fact]
    public void Launch_NoLoader_ReportsInsteadOfStartingTheGame()
    {
        var result = _launcher.Launch(_instance, loader: null);

        Assert.Equal(LaunchOutcome.NoLoader, result.Outcome);
        Assert.False(result.Started);
        Assert.Empty(_starter.Plans);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_ListingWithoutLaunchTarget_ReportsIt()
    {
        PlaceStarMap();

        var result = _launcher.Launch(_instance, LoaderListing(provides: new LoaderProvides(contentDir: InstallAnchor.Mods), standalone: false));

        Assert.Equal(LaunchOutcome.NoLaunchTarget, result.Outcome);
        Assert.Contains("StarMap", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_ListingWithoutProvides_ReportsNoLaunchTarget()
    {
        PlaceStarMap();

        var result = _launcher.Launch(_instance, LoaderListing(provides: null, standalone: false));

        Assert.Equal(LaunchOutcome.NoLaunchTarget, result.Outcome);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_LoaderWhoseHandoverIsUnknown_ReportsInsteadOfGuessing()
    {
        var result = _launcher.Launch(_instance, LoaderListing(modId: "OtherLoader", provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.NoInstanceHandover, result.Outcome);
        Assert.Contains("StarMap", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_NoLoaderDirectoryConfigured_ReportsIt()
    {
        // The real provider with no loader directories, so StarMap is known but not located.
        using var launcher = new LoaderLauncher(new GamePathProvider(gameDirectory: null), _starter);

        var result = launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.NoLoaderDirectory, result.Outcome);
        Assert.Contains("StarMap", result.Message);
        Assert.Empty(_starter.Plans);
    }

    [Fact]
    public void Launch_ExecutableMissing_ReportsIt()
    {
        Directory.CreateDirectory(StarMapDirectory);

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.LaunchTargetMissing, result.Outcome);
        Assert.Contains(Path.Combine(StarMapDirectory, "StarMap.exe"), result.Message);
        Assert.Equal(Path.Combine(Path.GetFullPath(StarMapDirectory), "StarMap.exe"), result.Plan!.Executable);
        Assert.Empty(_starter.Plans);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_SecondLaunchWhileTheFirstRuns_IsRefused()
    {
        PlaceStarMap();
        var listing = LoaderListing(provides: StarMapProvides());
        _launcher.Launch(_instance, listing);

        var second = _launcher.Launch(_instance, listing);

        Assert.Equal(LaunchOutcome.AlreadyRunning, second.Outcome);
        Assert.Contains("Test", second.Message);
        Assert.Single(_starter.Plans);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_AfterTheProcessExited_StartsAgainAndReleasesTheOldHandle()
    {
        PlaceStarMap();
        var listing = LoaderListing(provides: StarMapProvides());
        _launcher.Launch(_instance, listing);
        var first = Assert.Single(_starter.Processes);

        first.HasExited = true;

        Assert.False(_launcher.IsRunning(_instance.InstanceId));
        Assert.True(first.Disposed);

        var second = _launcher.Launch(_instance, listing);

        Assert.True(second.Started);
        Assert.Equal(_starter.Processes[^1].Id, second.ProcessId);
        Assert.NotEqual(first.Id, second.ProcessId);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_TwoInstances_BothRun()
    {
        PlaceStarMap();
        var listing = LoaderListing(provides: StarMapProvides());
        var other = new Instance("Other", InstanceSource.Custom.Value);

        var first = _launcher.Launch(_instance, listing);
        var second = _launcher.Launch(other, listing);

        Assert.True(first.Started);
        Assert.True(second.Started);
        Assert.True(_launcher.IsRunning(_instance.InstanceId));
        Assert.True(_launcher.IsRunning(other.InstanceId));
        Assert.NotEqual(first.Plan!.Arguments[1], second.Plan!.Arguments[1]);
    }

    [Fact]
    public void Launch_SystemRefusesToStart_ReportsStartFailed()
    {
        PlaceStarMap();
        _starter.Failure = new Win32Exception("Access is denied.");

        var result = _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));

        Assert.Equal(LaunchOutcome.StartFailed, result.Outcome);
        Assert.Contains("Access is denied.", result.Message);
        Assert.Same(Assert.Single(_starter.Plans), result.Plan);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Launch_AModInsteadOfALoader_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _launcher.Launch(_instance, MetadataFixtures.MinimalMetadata()));
    }

    [Fact]
    public void Launch_NullInstance_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _launcher.Launch(null!, LoaderListing(provides: StarMapProvides())));
    }

    [Fact]
    public void IsRunning_UnknownInstance_IsFalse()
    {
        Assert.False(_launcher.IsRunning(Guid.NewGuid()));
    }

    [Fact]
    public void Dispose_ReleasesTheHandlesAndForgetsTheLaunches()
    {
        PlaceStarMap();
        _launcher.Launch(_instance, LoaderListing(provides: StarMapProvides()));
        var process = Assert.Single(_starter.Processes);

        _launcher.Dispose();

        Assert.True(process.Disposed);
        Assert.False(_launcher.IsRunning(_instance.InstanceId));
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(null!, _starter));
        Assert.Throws<ArgumentNullException>(() => new LoaderLauncher(_paths, null!));
    }

    public void Dispose()
    {
        _launcher.Dispose();

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
