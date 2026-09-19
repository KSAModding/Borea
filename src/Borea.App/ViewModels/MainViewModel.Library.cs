using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Instances;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum LibrarySort
{
    Name,
    LastPlayed,
}

/// <summary>
/// The order of the Library list, and when each instance was last played.
/// </summary>
public partial class MainViewModel
{
    private LibrarySort _librarySort;

    /// <summary>The Library rows below the active instance, in the chosen order. Every row when no instance is active.</summary>
    public ObservableCollection<InstanceItem> OtherInstances { get; } = [];

    public LibrarySort LibrarySort => _librarySort;

    public string LibrarySortText => _librarySort == LibrarySort.LastPlayed ? Localization.LibrarySortLastPlayed : Localization.LibrarySortName;

    [RelayCommand]
    private void SelectLibrarySort(LibrarySort sort)
    {
        if (sort == _librarySort)
            return;

        _librarySort = sort;
        OnPropertyChanged(nameof(LibrarySort));
        OnPropertyChanged(nameof(LibrarySortText));
        SortInstances();
    }

    private IEnumerable<InstanceItem> Sorted(IEnumerable<InstanceItem> items) => _librarySort switch
    {
        LibrarySort.LastPlayed => items
            .OrderBy(item => item.LastPlayedAt is null)
            .ThenByDescending(item => item.LastPlayedAt)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
        _ => items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CreatedAt),
    };

    /// <summary>Moves the rows in place, so a row that is being renamed keeps its state.</summary>
    private void SortInstances()
    {
        Arrange(Instances, Sorted(Instances).ToList());
        RefreshOtherInstances();
    }

    private void RefreshOtherInstances() => Arrange(OtherInstances, Instances.Where(item => !item.IsActive).ToList());

    private async Task<DateTimeOffset?> LastPlayedAsync(Instance instance)
        => _services is null
            ? instance.LastPlayedAt
            : instance.LastPlayedWith(await _services.GameLog.GetLastWriteAsync(instance.InstanceId));

    private async Task RefreshLastPlayedAsync(Guid instanceId)
    {
        if (_services is null || Instances.FirstOrDefault(item => item.InstanceId == instanceId) is not { } row)
            return;

        if (await _services.Instances.GetByIdAsync(instanceId) is { } instance)
        {
            row.LastPlayedAt = await LastPlayedAsync(instance);
            SortInstances();
        }
    }

    private void RefreshLibraryText() => OnPropertyChanged(nameof(LibrarySortText));
}
