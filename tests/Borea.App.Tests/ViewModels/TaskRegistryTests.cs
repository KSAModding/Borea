using System.Globalization;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.History;

namespace Borea.App.Tests.ViewModels;

public sealed class TaskRegistryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly LocalizationService _localization = new(CultureInfo.GetCultureInfo("en"));

    [Fact]
    public async Task End_WhileTheHistoryLoads_SavesTheNewTaskAboveTheLoadedOnes()
    {
        var repository = new FakeRepository([new(TaskKind.ModRemoval, TaskState.Finished, "MeasureTools", null, null, Start, Start)]);
        var registry = Registry(repository);

        var loading = registry.LoadAsync();
        registry.End(Run(registry, TaskKind.IndexRefresh), TaskState.Finished);
        Assert.Empty(repository.Saves);
        repository.Reading.SetResult();
        await loading;
        await registry.WhenSavedAsync();

        Assert.Equal([TaskKind.IndexRefresh, TaskKind.ModRemoval], registry.History.Select(task => task.Kind));
        Assert.Equal([TaskKind.IndexRefresh, TaskKind.ModRemoval], Assert.Single(repository.Saves).Select(entry => entry.Kind));
    }

    [Fact]
    public async Task Save_ThatFails_DoesNotStopTheNextSave()
    {
        var repository = new FakeRepository([]) { SaveFailure = new InvalidOperationException("The disk is gone.") };
        repository.Reading.SetResult();
        var registry = Registry(repository);
        await registry.LoadAsync();

        registry.End(Run(registry, TaskKind.IndexRefresh), TaskState.Failed, "offline");
        registry.End(Run(registry, TaskKind.IndexRefresh), TaskState.Finished);
        await registry.WhenSavedAsync();

        Assert.Equal(2, Assert.Single(repository.Saves).Count);
    }

    [Fact]
    public void End_OverTheCap_DropsTheOldestTask()
    {
        var registry = Registry(null);

        for (var index = 0; index <= TaskHistoryEntry.MaxEntries; index++)
            registry.End(registry.Start(TaskKind.ModRemoval, $"Mod{index}", null, null, null, null, TaskState.Running), TaskState.Finished);

        Assert.Equal(TaskHistoryEntry.MaxEntries, registry.History.Count);
        Assert.Equal($"Mod{TaskHistoryEntry.MaxEntries}", registry.History[0].Subject);
        Assert.Equal("Mod1", registry.History[^1].Subject);
    }

    [Fact]
    public void TryAgain_WhileTheSameWorkRuns_IsDisabled()
    {
        var registry = Registry(null);
        var failed = Run(registry, TaskKind.IndexRefresh);
        registry.End(failed, TaskState.Failed, "offline");
        Assert.True(failed.RetryCommand.CanExecute(null));

        var running = Run(registry, TaskKind.IndexRefresh);
        Assert.True(failed.HasRetry);
        Assert.False(failed.RetryCommand.CanExecute(null));

        registry.End(running, TaskState.Finished);
        Assert.True(failed.RetryCommand.CanExecute(null));
    }

    [Fact]
    public void TryAgain_OfAnUpdate_WaitsOnlyForTheUpdatesOfItsInstance()
    {
        var registry = Registry(null);
        var main = Guid.NewGuid();
        var failed = registry.Start(TaskKind.UpdateAll, null, main, "Main", null, null, TaskState.Running);
        registry.End(failed, TaskState.Failed, "offline");

        registry.Start(TaskKind.Update, "MeasureTools", Guid.NewGuid(), "Other", "MeasureTools", null, TaskState.Running);
        Assert.True(failed.CanRetry);

        registry.Start(TaskKind.Update, "MeasureTools", main, "Main", "MeasureTools", null, TaskState.Running);
        Assert.False(failed.CanRetry);
    }

    private TaskRegistry Registry(FakeRepository? repository)
        => new(_localization, () => repository, () => null, _ => Task.CompletedTask);

    private static TaskItem Run(TaskRegistry registry, TaskKind kind)
        => registry.Start(kind, null, null, null, null, null, TaskState.Running);

    private sealed class FakeRepository(IReadOnlyList<TaskHistoryEntry> entries) : ITaskHistoryRepository
    {
        public TaskCompletionSource Reading { get; } = new();

        public List<IReadOnlyList<TaskHistoryEntry>> Saves { get; } = [];

        public Exception? SaveFailure { get; set; }

        public async Task<IReadOnlyList<TaskHistoryEntry>> GetAsync(CancellationToken cancellationToken = default)
        {
            await Reading.Task;
            return entries;
        }

        public Task SaveAsync(IReadOnlyList<TaskHistoryEntry> saved, CancellationToken cancellationToken = default)
        {
            if (SaveFailure is { } failure)
            {
                SaveFailure = null;
                throw failure;
            }

            Saves.Add(saved);
            return Task.CompletedTask;
        }
    }
}
