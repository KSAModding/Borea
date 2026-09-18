using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The toasts of the tasks that ended, and the pages their actions open.
/// </summary>
public partial class MainViewModel
{
    public ToastService Toasts { get; }

    /// <summary>The task the task drawer scrolls to.</summary>
    [ObservableProperty]
    private TaskItem? _taskInView;

    internal bool HasInstance(Guid instanceId) => Instances.Any(instance => instance.InstanceId == instanceId);

    internal Task OpenInstanceByIdAsync(Guid instanceId)
        => Instances.FirstOrDefault(instance => instance.InstanceId == instanceId) is { } instance ? OpenInstanceAsync(instance) : Task.CompletedTask;

    internal void ShowTaskDetails(TaskItem task)
    {
        // the drawer opens before the task is set, so it scrolls to the task after it scrolled to its end
        IsTasksOpen = true;
        // the drawer scrolls again when the same task is asked for twice
        TaskInView = null;
        TaskInView = task;
    }
}
