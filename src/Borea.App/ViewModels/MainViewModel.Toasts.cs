using System;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The toasts of the tasks that ended and of short results, and the pages their actions open.
/// </summary>
public partial class MainViewModel
{
    public ToastService Toasts { get; }

    /// <summary>The task the task drawer scrolls to.</summary>
    [ObservableProperty]
    private TaskItem? _taskInView;

    /// <summary>The error toast whose details the modal shows. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToastDetailsOpen))]
    private ToastItem? _toastInDetails;

    [ObservableProperty]
    private string? _toastDetailsError;

    public bool IsToastDetailsOpen => ToastInDetails is not null;

    internal bool HasInstance(Guid instanceId) => Instances.Any(instance => instance.InstanceId == instanceId);

    /// <summary>Opens the instance, and marks the row of the vehicle or save of <paramref name="contentId"/> when the instance holds it.</summary>
    internal async Task OpenInstanceByIdAsync(Guid instanceId, string? contentId)
    {
        if (Instances.FirstOrDefault(instance => instance.InstanceId == instanceId) is not { } instance)
            return;

        await OpenInstanceAsync(instance);
        if (VehiclesSection.Items.Concat(SavesSection.Items).FirstOrDefault(row => row.Record is { } record && ModIds.Equals(record.ListingId, contentId)) is { } target)
            MarkGameSave(target);
    }

    internal void ShowTaskDetails(TaskItem task)
    {
        // the drawer opens before the task is set, so it scrolls to the task after it scrolled to its end
        IsTasksOpen = true;
        // the drawer scrolls again when the same task is asked for twice
        TaskInView = null;
        TaskInView = task;
    }

    internal void ShowSuccessToast(Func<string> message, string? detail = null) => Toasts.ShowMessage(ToastKind.Success, message, detail);

    internal void ShowErrorToast(Func<string> message, string? reason = null)
    {
        var toast = Toasts.ShowMessage(ToastKind.Error, message, reason);
        _services?.Log.Write(reason is null ? toast.Message : $"{toast.Message}: {reason}");
    }

    internal void ShowToastDetails(ToastItem toast)
    {
        ToastDetailsError = null;
        ToastInDetails = toast;
    }

    [RelayCommand]
    private void CloseToastDetails() => ToastInDetails = null;

    [RelayCommand]
    private void OpenBoreaLogFromToast()
    {
        if (_services is not null)
            ToastDetailsError = TryOpenWithSystem(_services.Log.CurrentFilePath);
    }
}
