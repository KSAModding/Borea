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

    private sealed class WatchedLauncher(LaunchResult watched) : ILauncher
    {
        public LaunchResult Launch(Instance instance, ModMetadata? loader) => throw new NotSupportedException();

        public Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default) => Task.FromResult(watched);

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

        public Task<Instance> CreateAsync(string name, InstanceSource source) => throw new NotSupportedException();

        public Task RenameAsync(Guid instanceId, string newName) => throw new NotSupportedException();

        public Task DeleteAsync(Guid instanceId) => throw new NotSupportedException();

        public Task SaveAsync(Instance instance) => throw new NotSupportedException();
    }
}
