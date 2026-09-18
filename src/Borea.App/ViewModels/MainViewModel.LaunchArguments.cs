using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Launch;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The modal that edits the launch arguments of an instance.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The row whose launch arguments the modal edits. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLaunchArgumentsModalOpen))]
    private InstanceItem? _editingLaunchArguments;

    /// <summary>The arguments as one line, quoted the way <see cref="ArgumentLine"/> splits them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModalLaunchArgumentItems), nameof(HasModalLaunchArguments))]
    private string _modalLaunchArguments = string.Empty;

    public bool IsLaunchArgumentsModalOpen => EditingLaunchArguments is not null;

    /// <summary>The arguments the line splits into, with an empty argument shown as its quotes.</summary>
    public IReadOnlyList<string> ModalLaunchArgumentItems => ArgumentLine.Split(ModalLaunchArguments)
        .Select(argument => argument.Length == 0 ? "\"\"" : argument)
        .ToList();

    public bool HasModalLaunchArguments => ModalLaunchArgumentItems.Count > 0;

    internal void BeginEditLaunchArguments(InstanceItem item)
    {
        item.IsConfirmingDelete = false;
        InstanceError = null;
        ModalLaunchArguments = ArgumentLine.Join(item.LaunchArguments);
        EditingLaunchArguments = item;
    }

    [RelayCommand]
    private void CancelLaunchArgumentsModal()
    {
        EditingLaunchArguments = null;
        InstanceError = null;
    }

    /// <summary>
    /// Keeps the modal open with the error when an argument is refused or the
    /// save fails. A modal that was closed or opened again while the listings
    /// were read saves nothing.
    /// </summary>
    [RelayCommand]
    private async Task SaveLaunchArgumentsAsync()
    {
        if (EditingLaunchArguments is not { } item)
            return;

        var line = ModalLaunchArguments;
        var arguments = ArgumentLine.Split(line);
        var refused = await FindHandoverFlagAsync(arguments);
        if (EditingLaunchArguments != item || ModalLaunchArguments != line)
            return;

        if (refused is { } found)
        {
            InstanceError = Localization.FormatLaunchArgumentsHandoverFlag(found.Loader, found.Flag);
            return;
        }

        await RunInstanceOperationAsync(instances => instances.UpdateAsync(item.InstanceId, instance =>
        {
            instance.SetLaunchArguments(arguments);
            return true;
        }));

        if (InstanceError is null && EditingLaunchArguments == item)
            EditingLaunchArguments = null;
    }

    /// <summary>
    /// The first argument that the listing of a configured mod loader reads as
    /// its instance flag, from the cached index. A launch checks the listing it
    /// uses again, so a listing that cannot be read now refuses nothing.
    /// </summary>
    private async Task<(string Loader, string Flag)?> FindHandoverFlagAsync(IReadOnlyList<string> arguments)
    {
        if (_services is null || arguments.Count == 0)
            return null;

        foreach (var loaderId in _services.Settings.LoaderInstallations.Keys)
        {
            ModMetadata? listing;
            try
            {
                listing = await _services.ReadOnlyMods.GetListingAsync(loaderId);
            }
            catch (Exception)
            {
                continue;
            }

            if (listing is { Type: ContentType.ModLoader, Provides.Instance: { } handover } && handover.FlagIn(arguments) is { } flag)
                return (listing.Name, flag);
        }

        return null;
    }
}
