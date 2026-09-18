using System;
using System.IO;
using System.Threading.Tasks;
using Borea.Core.Instances;

namespace Borea.App.ViewModels;

/// <summary>
/// What the shown instance takes on disk: the total in the header, and each
/// mod's folder on its row. Measured after the rows are built, because a big
/// instance takes a moment to walk.
/// </summary>
public partial class MainViewModel
{
    private (Guid InstanceId, InstanceSizes Sizes)? _instanceSizes;

    private Task _instanceSizeLoad = Task.CompletedTask;

    /// <summary>The size of the whole instance folder, or null until it is measured.</summary>
    public string? InstanceSizeText
        => _instanceSizes is { } read && read.InstanceId == SelectedInstance?.InstanceId ? SizeText(read.Sizes.TotalBytes) : null;

    internal Task WhenInstanceSizesLoadedAsync() => _instanceSizeLoad;

    private void StartInstanceSizeLoad(Guid instanceId)
    {
        OnPropertyChanged(nameof(InstanceSizeText));
        var previous = _instanceSizeLoad;
        var load = LoadInstanceSizesAsync(instanceId);
        _instanceSizeLoad = previous.IsCompleted ? load : Task.WhenAll(previous, load);
    }

    private async Task LoadInstanceSizesAsync(Guid instanceId)
    {
        if (_services is not { } services)
            return;

        InstanceSizes sizes;
        try
        {
            sizes = await services.InstanceSizes.ReadAsync(instanceId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (SelectedInstance?.InstanceId != instanceId)
            return;

        _instanceSizes = (instanceId, sizes);
        OnPropertyChanged(nameof(InstanceSizeText));
        foreach (var item in _content)
            item.SizeText = sizes.ModBytes.TryGetValue(item.ModId, out var bytes) ? SizeText(bytes) : null;
    }
}
