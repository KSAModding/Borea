using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Launch;

public sealed class LastPlayedLauncherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 11, 24, 36, TimeSpan.Zero);

    private readonly Instance _instance = new("Main", InstanceSource.Custom.Value);

    [Fact]
    public async Task WatchStart_GameStarted_SavesWhenTheInstanceWasPlayed()
    {
        var instances = new MemoryInstances(_instance);
        var started = Started();
        var launcher = new LastPlayedLauncher(new WatchedLauncher(started), instances, new FixedTime(Now));

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.Same(started, result);
        Assert.Equal(Now, instances.Saved?.LastPlayedAt);
    }

    [Fact]
    public async Task WatchStart_LoaderExitedEarly_SavesNothing()
    {
        var instances = new MemoryInstances(_instance);
        var exited = LaunchResult.ExitedEarly(Plan(), -532462766, ["Unhandled exception."], blamedModId: null, "StarMap stopped right after starting.");
        var launcher = new LastPlayedLauncher(new WatchedLauncher(exited), instances, new FixedTime(Now));

        var result = await launcher.WatchStartAsync(_instance, Started());

        Assert.Same(exited, result);
        Assert.Null(instances.Saved);
    }

    [Fact]
    public async Task WatchStart_RecordCannotBeSaved_StillReportsTheStart()
    {
        var instances = new MemoryInstances(_instance) { Failure = new IOException("instance.toml is locked.") };
        var started = Started();
        var launcher = new LastPlayedLauncher(new WatchedLauncher(started), instances, new FixedTime(Now));

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.True(result.Started);
        Assert.Null(instances.Saved);
    }

    [Fact]
    public void Launch_GivenLoaderFolder_PassesItAndItsAdoptionOn()
    {
        var inner = new DirectoryRecorder();
        ILauncher launcher = new LastPlayedLauncher(inner, new MemoryInstances(_instance), new FixedTime(Now));

        launcher.Launch(_instance, loader: null, "D:/Loaders/StarMap/0.4.7", arguments: null, isAdopted: true);

        Assert.Equal(["D:/Loaders/StarMap/0.4.7"], inner.Directories);
        Assert.Equal([true], inner.Adoptions);
    }

    private static LaunchResult Started() => LaunchResult.Success(Plan(), 42, "Started StarMap.");

    private static LaunchPlan Plan()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Instances", "one"));
        var loader = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StarMap"));
        return new LaunchPlan(Path.Combine(loader, "StarMap.exe"), ["-InstancePath", root], loader, new Dictionary<string, string>());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Records the loader folder of each launch, the way only a launcher that implements the overload sees it.</summary>
    private sealed class DirectoryRecorder : ILauncher
    {
        public List<string?> Directories { get; } = [];

        public List<bool> Adoptions { get; } = [];

        public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null) =>
            Launch(instance, loader, loaderDirectory: null, arguments);

        public LaunchResult Launch(Instance instance, ModMetadata? loader, string? loaderDirectory, IReadOnlyList<string>? arguments, bool isAdopted = false)
        {
            Directories.Add(loaderDirectory);
            Adoptions.Add(isAdopted);
            return LaunchResult.Failed(LaunchOutcome.NoLaunchTarget, "Not started.");
        }

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(started);

        public Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(GameExit.Unknown);

        public bool IsRunning(Guid instanceId) => false;
    }

    private sealed class WatchedLauncher(LaunchResult watched) : ILauncher
    {
        public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null) => throw new NotSupportedException();

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(watched);

        public Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(GameExit.Unknown);

        public bool IsRunning(Guid instanceId) => false;
    }

    private sealed class MemoryInstances(Instance instance) : IInstanceRepository
    {
        public Exception? Failure { get; init; }

        public Instance? Saved { get; private set; }

        public Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            var result = update(instance);
            Saved = instance;
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<Instance>> GetAllAsync() => throw new NotSupportedException();

        public Task<Instance?> GetByIdAsync(Guid instanceId) => throw new NotSupportedException();

        public Task<Guid?> GetActiveInstanceIdAsync() => throw new NotSupportedException();

        public Task SetActiveInstanceAsync(Guid instanceId) => throw new NotSupportedException();

        public Task ClearActiveInstanceAsync() => throw new NotSupportedException();

        public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => throw new NotSupportedException();

        public Task<InstanceCreateResult> CreateAsync(string name, InstanceSource source) => throw new NotSupportedException();

        public Task<InstanceCreateResult> CreateAsync(Instance instance) => throw new NotSupportedException();

        public Task<InstanceCreateResult> CreateAsync(Instance instance, InstanceOrigin origin) => throw new NotSupportedException();

        public Task RenameAsync(Guid instanceId, string newName) => throw new NotSupportedException();

        public Task DeleteAsync(Guid instanceId) => throw new NotSupportedException();

        public Task SaveAsync(Instance instance) => throw new NotSupportedException();
    }
}
