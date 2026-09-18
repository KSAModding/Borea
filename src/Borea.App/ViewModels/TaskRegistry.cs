using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.History;
using Borea.Core.Logging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// Every long operation of the App reports here. It keeps what runs or waits
/// now and the history of what ended, and saves that history.
/// </summary>
public sealed partial class TaskRegistry : ObservableObject
{
    private readonly Func<ITaskHistoryRepository?> _repository;
    private readonly Func<IBoreaLog?> _log;
    private readonly Func<TaskItem, Task> _retry;
    private Task _loading = Task.CompletedTask;
    private Task _saves = Task.CompletedTask;
    private bool _isLoadStarted;

    internal TaskRegistry(LocalizationService localization, Func<ITaskHistoryRepository?> repository, Func<IBoreaLog?> log, Func<TaskItem, Task> retry)
    {
        Localization = localization;
        _repository = repository;
        _log = log;
        _retry = retry;
    }

    internal LocalizationService Localization { get; }

    /// <summary>Raised once for each task that ends, after it moved to the history.</summary>
    internal event Action<TaskItem>? Ended;

    /// <summary>The running and waiting tasks, in the order they started.</summary>
    public ObservableCollection<TaskItem> Running { get; } = [];

    /// <summary>The tasks that ended, newest first.</summary>
    public ObservableCollection<TaskItem> History { get; } = [];

    public bool IsBusy => Running.Count > 0;

    public bool HasHistory => History.Count > 0;

    public bool IsEmpty => !IsBusy && !HasHistory;

    public bool HasProgress => Running.Any(task => task.HasProgress);

    /// <summary>The mean progress of the running tasks that know how far they are.</summary>
    public double Progress => HasProgress ? Running.Where(task => task.HasProgress).Average(task => task.Progress) : 0;

    internal TaskItem Start(TaskKind kind, string? subject, Guid? instanceId, string? instanceName, string? contentId, string? version, TaskState state)
    {
        var task = new TaskItem(this, kind, state, subject, instanceId, instanceName, contentId, version, DateTimeOffset.UtcNow);
        task.PropertyChanged += OnRunningTaskChanged;
        Running.Add(task);
        OnRunningChanged();
        return task;
    }

    internal void End(TaskItem task, TaskState state, string? failureReason = null)
    {
        if (!Leave(task))
            return;

        task.End(state, failureReason, DateTimeOffset.UtcNow);
        History.Insert(0, task);
        while (History.Count > TaskHistoryEntry.MaxEntries)
            History.RemoveAt(History.Count - 1);

        OnHistoryChanged();
        _saves = SaveAfterAsync(_saves, _isLoadStarted && _loading.IsCompleted ? Entries() : null);
        Ended?.Invoke(task);
    }

    /// <summary>Drops a task that ended without doing anything, such as a plan that waits for a confirmation.</summary>
    internal void Discard(TaskItem task) => Leave(task);

    /// <summary>Whether a task that does the same work as <paramref name="task"/> runs or waits now.</summary>
    internal bool IsBusyWith(TaskItem task) => Running.Any(running => running.DoesSameWorkAs(task));

    internal Task RetryAsync(TaskItem task) => _retry(task);

    /// <summary>Reads the saved history once, below the tasks that ended before.</summary>
    internal Task LoadAsync()
    {
        if (!_isLoadStarted && _repository() is { } repository)
        {
            _isLoadStarted = true;
            _loading = ReadAsync(repository);
        }

        return _loading;
    }

    private async Task ReadAsync(ITaskHistoryRepository repository)
    {
        try
        {
            foreach (var entry in await repository.GetAsync())
            {
                if (History.Count >= TaskHistoryEntry.MaxEntries)
                    break;

                History.Add(TaskItem.FromEntry(this, entry));
            }
        }
        catch (Exception exception)
        {
            _log()?.Write("The task history could not be read.", exception);
        }

        OnHistoryChanged();
    }

    /// <summary>Completes when every save queued so far has finished.</summary>
    internal Task WhenSavedAsync() => _saves;

    internal void RefreshText()
    {
        foreach (var task in Running.Concat(History))
            task.RefreshText();
    }

    private bool Leave(TaskItem task)
    {
        if (!Running.Remove(task))
            return false;

        task.PropertyChanged -= OnRunningTaskChanged;
        OnRunningChanged();
        return true;
    }

    private List<TaskHistoryEntry> Entries() => History.Select(task => task.ToEntry()).ToList();

    /// <summary>Reads the history first, so a save never replaces entries that were not read yet.</summary>
    /// <param name="entries">The history when the task ended. Null when it ended before the history was read.</param>
    private async Task SaveAfterAsync(Task previous, IReadOnlyList<TaskHistoryEntry>? entries)
    {
        await previous;
        await LoadAsync();
        if (_repository() is not { } repository)
            return;

        try
        {
            await repository.SaveAsync(entries ?? Entries());
        }
        catch (Exception exception)
        {
            _log()?.Write("The task history could not be saved.", exception);
        }
    }

    private void OnRunningTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskItem.Progress) or nameof(TaskItem.HasProgress))
        {
            OnPropertyChanged(nameof(HasProgress));
            OnPropertyChanged(nameof(Progress));
        }
    }

    private void OnRunningChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(Progress));
        foreach (var task in History.Where(task => task.IsFailed))
            task.RefreshCanRetry();
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
