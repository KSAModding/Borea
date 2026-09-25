using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.History;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// Shows a toast for the tasks of the <see cref="TaskRegistry"/> that end, and for short results.
/// </summary>
public sealed partial class ToastService : ObservableObject
{
    internal const int MaxShown = 3;

    internal static readonly TimeSpan FinishedDuration = TimeSpan.FromSeconds(5);

    internal static readonly TimeSpan ProblemDuration = TimeSpan.FromSeconds(10);

    private readonly MainViewModel _owner;
    private ToastItem? _announced;

    internal ToastService(MainViewModel owner)
    {
        _owner = owner;
        owner.Tasks.Ended += Show;
        owner.Instances.CollectionChanged += OnInstancesChanged;
    }

    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    internal LocalizationService Localization => _owner.Localization;

    /// <summary>The shown toasts, newest last.</summary>
    public ObservableCollection<ToastItem> Items { get; } = [];

    /// <summary>The text of the newest toast, for the live region that screen readers announce.</summary>
    [ObservableProperty]
    private string? _announcement;

    /// <summary>
    /// A finished index refresh or an update that changed nothing has nothing
    /// to say, and only a closing window stops a mod list import or a replace.
    /// The Home banner says what a Borea update that did not fail does next.
    /// </summary>
    private static bool HasToast(TaskItem task) => task switch
    {
        { State: TaskState.Finished, Kind: TaskKind.IndexRefresh } => false,
        { State: TaskState.Finished, Kind: TaskKind.Update or TaskKind.UpdateAll, ModCount: 0 } => false,
        { State: TaskState.Stopped, Kind: TaskKind.ModListImport or TaskKind.ManualReplace } => false,
        { State: not TaskState.Failed, Kind: TaskKind.BoreaUpdate } => false,
        _ => true,
    };

    private void Show(TaskItem task)
    {
        if (HasToast(task))
            Show(new ToastItem(this, task));
    }

    /// <summary>Shows a short result that belongs to no task.</summary>
    internal ToastItem ShowMessage(ToastKind kind, Func<string> message, string? detail = null, ToastAction? action = null)
    {
        var toast = new ToastItem(this, kind, message, detail, action);
        Show(toast);
        return toast;
    }

    private void Show(ToastItem toast)
    {
        Items.Add(toast);
        while (Items.Count > MaxShown)
            Close(Items[0]);

        // a screen reader announces only a changed text
        if (Announcement == toast.AnnouncementText)
            Announcement = null;
        Announcement = toast.AnnouncementText;
        _announced = toast;
        toast.RestartTimer();
    }

    internal void Close(ToastItem toast)
    {
        if (!Items.Remove(toast))
            return;

        toast.StopTimer();
        if (ReferenceEquals(_announced, toast))
        {
            _announced = null;
            Announcement = null;
        }
    }

    internal bool HasInstance(Guid? instanceId) => instanceId is { } id && _owner.HasInstance(id);

    internal Task OpenInstanceAsync(ToastItem toast)
    {
        Close(toast);
        return toast.TaskItem?.InstanceId is { } instanceId ? _owner.OpenInstanceByIdAsync(instanceId) : Task.CompletedTask;
    }

    internal void ShowDetails(ToastItem toast)
    {
        Close(toast);
        if (toast.TaskItem is { } task)
            _owner.ShowTaskDetails(task);
        else
            _owner.ShowToastDetails(toast);
    }

    internal void RefreshText()
    {
        foreach (var toast in Items)
            toast.RefreshText();
    }

    private void OnInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var toast in Items)
            toast.RefreshCanOpenInstance();
    }
}
