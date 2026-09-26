using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The hint the pages show while no instance is active. Without any instance it
/// leads to the New instance modal, otherwise to the Library. With an active
/// instance, the pages and the add buttons name it instead.
/// </summary>
public partial class MainViewModel
{
    internal bool HasInstances => Instances.Count > 0;

    public string HomeInstanceHintText => HasInstances ? Localization.HomeNoActiveInstance : Localization.HomeNoInstance;

    public string InstanceHintText => HasInstances ? Localization.DiscoverNoActiveInstance : Localization.DiscoverNoInstance;

    public string InstanceHintActionText => HasInstances ? Localization.DiscoverOpenLibrary : Localization.DiscoverCreateInstance;

    public string? AddingToText => ActiveInstance is { } active ? Localization.FormatDiscoverAddingTo(active.Name) : null;

    public string AddingToBefore => Localization.SplitDiscoverAddingTo().Before;

    public string AddingToAfter => Localization.SplitDiscoverAddingTo().After;

    public string AddToText => ActiveInstance is { } active ? Localization.FormatDiscoverAddTo(active.Name) : Localization.DiscoverAdd;

    /// <summary>What Add does on a mod the active instance already holds in <paramref name="installedVersion"/>.</summary>
    public string ReplaceVersionText(string installedVersion) => ActiveInstance is { } active
        ? Localization.FormatContentReplaceVersionIn(installedVersion, active.Name)
        : Localization.FormatContentReplaceVersion(installedVersion);

    /// <summary>
    /// Says why an add without a target instance does nothing. The hint above a
    /// list is out of sight of a click on a row, so the toast carries it there,
    /// and the Library is where the player activates or creates an instance.
    /// </summary>
    internal void ShowInstanceHintToast()
        => Toasts.ShowMessage(ToastKind.Error, () => InstanceHintText, action: new ToastAction(() => Localization.DiscoverOpenLibrary, SetMainWindowLibrary));

    [RelayCommand]
    private void FollowInstanceHint()
    {
        if (HasInstances)
            SetMainWindowLibrary();
        else
            BeginCreateInstance();
    }

    private void RefreshInstanceHint()
    {
        OnPropertyChanged(nameof(HomeInstanceHintText));
        OnPropertyChanged(nameof(InstanceHintText));
        OnPropertyChanged(nameof(InstanceHintActionText));
        OnPropertyChanged(nameof(AddingToText));
        OnPropertyChanged(nameof(AddingToBefore));
        OnPropertyChanged(nameof(AddingToAfter));
        OnPropertyChanged(nameof(AddToText));
    }
}
