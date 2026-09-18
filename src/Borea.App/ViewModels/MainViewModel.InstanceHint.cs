using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The hint the pages show while no instance is active. Without any instance it
/// leads to the New instance modal, otherwise to the Library.
/// </summary>
public partial class MainViewModel
{
    internal bool HasInstances => Instances.Count > 0;

    public string HomeInstanceHintText => HasInstances ? Localization.HomeNoActiveInstance : Localization.HomeNoInstance;

    public string InstanceHintText => HasInstances ? Localization.DiscoverNoActiveInstance : Localization.DiscoverNoInstance;

    public string InstanceHintActionText => HasInstances ? Localization.DiscoverOpenLibrary : Localization.DiscoverCreateInstance;

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
    }
}
