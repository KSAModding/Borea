using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// One running install. Its Stop button and a closing window request the same
/// <see cref="InstallStop"/>.
/// </summary>
public sealed partial class InstallRun : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StopText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isStopping;

    /// <summary>True while the running mod is past its download, so a stop waits until that mod is finished.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StopText))]
    private bool _isFinishingMod;

    internal InstallRun(LocalizationService localization)
    {
        _localization = localization;
    }

    internal InstallStop InstallStop { get; } = new();

    internal Task Ended => _ended.Task;

    public string StopText => !IsStopping ? _localization.InstallStop
        : IsFinishingMod ? _localization.InstallStoppingAfterMod
        : _localization.InstallStopping;

    [RelayCommand(CanExecute = nameof(CanStop))]
    internal void Stop()
    {
        IsStopping = true;
        InstallStop.Request();
    }

    private bool CanStop() => !IsStopping;

    internal void RefreshText() => OnPropertyChanged(nameof(StopText));

    internal void End() => _ended.TrySetResult();
}
