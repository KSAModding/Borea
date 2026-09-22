using Borea.Core.Game;
using Borea.Core.State;

namespace Borea.Core.Tests.Game;

public sealed class CheckedModStateRepositoryTests
{
    private static readonly Guid Instance = Guid.NewGuid();

    [Fact]
    public async Task Reads_PassThroughOnABrokenShape()
    {
        var inner = new RecordingModStateRepository();
        var repository = new CheckedModStateRepository(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Broken));

        Assert.Empty(await repository.GetEntriesAsync(Instance));
        Assert.False(await repository.IsActiveAsync(Instance, "mod"));
        Assert.Empty(await repository.GetAllActiveModIdsAsync(Instance));
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Writes_RefuseOnABrokenShape(Func<IModStateRepository, Task> write)
    {
        var inner = new RecordingModStateRepository();
        var repository = new CheckedModStateRepository(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Broken));

        var exception = await Assert.ThrowsAsync<GameShapeException>(() => write(repository));

        Assert.False(inner.Wrote);
        Assert.Equal(GameShapeStatus.Broken, exception.Shape.Status);
        Assert.Contains("KSA", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Writes_RunOnAShapeThatHolds(Func<IModStateRepository, Task> write)
    {
        var inner = new RecordingModStateRepository();
        var repository = new CheckedModStateRepository(inner, new FixedGameShapeCheck(FixedGameShapeCheck.Holds));

        await write(repository);

        Assert.True(inner.Wrote);
    }

    public static TheoryData<Func<IModStateRepository, Task>> Writes() => new()
    {
        repository => repository.AddEntryAsync(Instance, "mod", enabled: true),
        repository => repository.SetActiveAsync(Instance, "mod"),
        repository => repository.SetInactiveAsync(Instance, "mod"),
        repository => repository.ReorderAsync(Instance, ["mod"]),
        repository => repository.PutGameContentFirstAsync(Instance, "game"),
    };

    private sealed class RecordingModStateRepository : IModStateRepository
    {
        public bool Wrote { get; private set; }

        public Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModManifestEntry>>([]);

        public Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default)
            => Write(ModEntryAddResult.Added);

        public Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
            => Write(true);

        public Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
            => Write(true);

        public Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default)
            => Write(true);

        public Task<bool> PutGameContentFirstAsync(Guid instanceId, string gameDirectory, CancellationToken cancellationToken = default)
            => Write(true);

        private Task<T> Write<T>(T result)
        {
            Wrote = true;
            return Task.FromResult(result);
        }
    }
}
