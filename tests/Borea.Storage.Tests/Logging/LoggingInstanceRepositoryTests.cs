using Borea.Core.Instances;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Logging;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Logging;

public sealed class LoggingInstanceRepositoryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileInstanceRepository _files;
    private readonly RecordingLog _log = new();
    private readonly LoggingInstanceRepository _instances;

    public LoggingInstanceRepositoryTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _files = new FileInstanceRepository(_paths);
        _instances = new LoggingInstanceRepository(_files, _paths, _log);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task Create_New_WritesTheNameTheIdAndTheActivation()
    {
        var first = await _instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var second = await _instances.CreateAsync("Other", InstanceSource.Custom.Value);

        Assert.Equal(
            [
                $"Instance \"Main\" ({first.Instance.InstanceId}) created as a new instance and made active.",
                $"Instance \"Other\" ({second.Instance.InstanceId}) created as a new instance.",
            ],
            _log.Messages);
    }

    [Theory]
    [InlineData(InstanceOrigin.Duplicate, "as a duplicate")]
    [InlineData(InstanceOrigin.ModListImport, "from a modlist")]
    [InlineData(InstanceOrigin.GameProfileImport, "from the game profile")]
    public async Task Create_WithOrigin_NamesTheOrigin(InstanceOrigin origin, string text)
    {
        var instance = new Instance("Main", InstanceSource.Custom.Value);

        await _instances.CreateAsync(instance, origin);

        Assert.Equal($"Instance \"Main\" ({instance.InstanceId}) created {text} and made active.", Assert.Single(_log.Messages));
    }

    [Fact]
    public async Task Create_FromModPack_NamesThePack()
    {
        var created = await _instances.CreateAsync("Pack", new InstanceSource.FromModPack("ExamplePack", ModVersion.Parse("1.2.0")));

        Assert.Equal($"Instance \"Pack\" ({created.Instance.InstanceId}) created from mod pack ExamplePack 1.2.0 and made active.", Assert.Single(_log.Messages));
    }

    [Fact]
    public async Task Create_TakenName_WritesTheRefusalWithoutTheException()
    {
        await _instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var instance = new Instance("Main", InstanceSource.Custom.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _instances.CreateAsync(instance));

        Assert.Equal($"Creation of instance \"Main\" ({instance.InstanceId}) as a new instance refused: Instance name 'Main' is already in use.", _log.Messages[^1]);
        Assert.Null(_log.Exceptions[^1]);
    }

    [Fact]
    public async Task Rename_WritesTheOldAndTheNewName()
    {
        var id = (await _instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId;

        await _instances.RenameAsync(id, "Renamed");

        Assert.Equal($"Instance \"Main\" ({id}) renamed to \"Renamed\".", _log.Messages[^1]);
    }

    [Fact]
    public async Task Rename_TakenName_WritesTheRefusal()
    {
        await _instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var id = (await _instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance.InstanceId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _instances.RenameAsync(id, "Main"));

        Assert.Equal($"Rename of instance \"Other\" ({id}) to \"Main\" refused: Instance name 'Main' is already in use.", _log.Messages[^1]);
    }

    [Fact]
    public async Task Delete_WritesTheStartWithTheFolderAndTheResult()
    {
        var id = (await _instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId;

        await _instances.DeleteAsync(id);

        Assert.Equal(
            [
                $"Deletion of instance \"Main\" ({id}) in {_paths.GetInstanceRoot(id)} started.",
                $"Instance \"Main\" ({id}) deleted, no instance is active now.",
            ],
            _log.Messages.Skip(1));
    }

    [Fact]
    public async Task Delete_UnknownInstance_WritesThatNothingWasDeleted()
    {
        var id = Guid.NewGuid();

        await _instances.DeleteAsync(id);

        Assert.Equal($"Instance {id} not found, nothing deleted.", Assert.Single(_log.Messages));
    }

    [Fact]
    public async Task Delete_FailedPartWay_StillWritesTheStartAndTheFailure()
    {
        var id = (await _files.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId;
        var failure = new IOException("The file is in use.");
        var instances = new LoggingInstanceRepository(new FailingRepository(_files, _paths) { DeleteFailure = failure, DeleteMetadata = true }, _paths, _log);

        var thrown = await Assert.ThrowsAsync<IOException>(() => instances.DeleteAsync(id));

        Assert.Same(failure, thrown);
        Assert.Equal(
            [
                $"Deletion of instance \"Main\" ({id}) in {_paths.GetInstanceRoot(id)} started.",
                $"Deletion of instance \"Main\" ({id}) failed part way, Borea no longer lists it, no instance is active now.",
            ],
            _log.Messages);
        Assert.Same(failure, _log.Exceptions[^1]);
    }

    [Fact]
    public async Task Delete_FailedBeforeAnyChange_WritesTheFailure()
    {
        await _files.CreateAsync("Main", InstanceSource.Custom.Value);
        var id = (await _files.CreateAsync("Other", InstanceSource.Custom.Value)).Instance.InstanceId;
        var failure = new IOException("The file is in use.");
        var instances = new LoggingInstanceRepository(new FailingRepository(_files, _paths) { DeleteFailure = failure }, _paths, _log);

        await Assert.ThrowsAsync<IOException>(() => instances.DeleteAsync(id));

        Assert.Equal($"Deletion of instance \"Other\" ({id}) failed.", _log.Messages[^1]);
        Assert.Same(failure, _log.Exceptions[^1]);
    }

    [Fact]
    public async Task Delete_FailedAndTheRestCannotBeRead_SaysSo()
    {
        await _files.CreateAsync("Main", InstanceSource.Custom.Value);
        var id = (await _files.CreateAsync("Other", InstanceSource.Custom.Value)).Instance.InstanceId;
        var instances = new LoggingInstanceRepository(new FailingRepository(_files, _paths) { DeleteFailure = new IOException("The file is in use."), LookupFailsAfterDelete = true }, _paths, _log);

        await Assert.ThrowsAsync<IOException>(() => instances.DeleteAsync(id));

        Assert.Equal($"Deletion of instance \"Other\" ({id}) failed, Borea cannot read what is left.", _log.Messages[^1]);
    }

    [Fact]
    public async Task ActivateAndDeactivate_WriteOneLineEach()
    {
        await _instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var id = (await _instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance.InstanceId;
        _log.Messages.Clear();

        await _instances.SetActiveInstanceAsync(id);
        await _instances.ClearActiveInstanceAsync();
        await _instances.ClearActiveInstanceAsync();

        Assert.Equal(
            [
                $"Instance \"Other\" ({id}) activated.",
                $"Instance \"Other\" ({id}) deactivated.",
            ],
            _log.Messages);
    }

    [Fact]
    public async Task Activate_UnknownInstance_WritesTheRefusal()
    {
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _instances.SetActiveInstanceAsync(id));

        Assert.Equal($"Activation of instance {id} refused: No instance with ID '{id}' exists.", Assert.Single(_log.Messages));
    }

    [Fact]
    public async Task Deactivate_Failure_WritesTheFailure()
    {
        var id = (await _files.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId;
        var failure = new UnauthorizedAccessException("Access is denied.");
        var instances = new LoggingInstanceRepository(new FailingRepository(_files, _paths) { ClearFailure = failure }, _paths, _log);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(instances.ClearActiveInstanceAsync);

        Assert.Equal($"Deactivation of instance \"Main\" ({id}) failed.", Assert.Single(_log.Messages));
        Assert.Same(failure, _log.Exceptions[^1]);
    }

    private sealed class FailingRepository(IInstanceRepository inner, TestGamePathProvider paths) : IInstanceRepository
    {
        private bool _deleted;

        public Exception? DeleteFailure { get; init; }

        public bool DeleteMetadata { get; init; }

        public bool LookupFailsAfterDelete { get; init; }

        public Exception? ClearFailure { get; init; }

        public Task<IReadOnlyList<Instance>> GetAllAsync() => inner.GetAllAsync();

        public Task<Instance?> GetByIdAsync(Guid instanceId)
            => _deleted && LookupFailsAfterDelete ? Task.FromException<Instance?>(new IOException("The metadata is damaged.")) : inner.GetByIdAsync(instanceId);

        public Task<Guid?> GetActiveInstanceIdAsync() => inner.GetActiveInstanceIdAsync();

        public Task SetActiveInstanceAsync(Guid instanceId) => inner.SetActiveInstanceAsync(instanceId);

        public Task ClearActiveInstanceAsync() => ClearFailure is null ? inner.ClearActiveInstanceAsync() : Task.FromException(ClearFailure);

        public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => inner.IsNameAvailableAsync(name, excludingInstanceId);

        public Task<InstanceCreateResult> CreateAsync(string name, InstanceSource source) => inner.CreateAsync(name, source);

        public Task<InstanceCreateResult> CreateAsync(Instance instance) => inner.CreateAsync(instance);

        public Task<InstanceCreateResult> CreateAsync(Instance instance, InstanceOrigin origin) => inner.CreateAsync(instance, origin);

        public Task RenameAsync(Guid instanceId, string newName) => inner.RenameAsync(instanceId, newName);

        public async Task DeleteAsync(Guid instanceId)
        {
            _deleted = true;
            if (DeleteMetadata)
            {
                File.Delete(paths.GetInstanceMetadataPath(instanceId));
                if (await inner.GetActiveInstanceIdAsync() == instanceId)
                    await inner.ClearActiveInstanceAsync();
            }

            if (DeleteFailure is not null)
                throw DeleteFailure;
            await inner.DeleteAsync(instanceId);
        }

        public Task SaveAsync(Instance instance) => inner.SaveAsync(instance);

        public Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(instanceId, update, cancellationToken);
    }
}
