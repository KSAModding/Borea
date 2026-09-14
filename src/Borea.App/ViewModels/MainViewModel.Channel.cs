using System;
using System.Collections.Generic;
using System.Linq;
using Borea.App.Localization;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The release channels, as the settings and the Show filter name them.
/// </summary>
public partial class MainViewModel
{
    private IReadOnlyList<ReleaseChannelOption>? _releaseChannelOptions;

    public IReadOnlyList<ReleaseChannelOption> ReleaseChannelOptions
        => _releaseChannelOptions ??= Enum.GetValues<ReleaseChannel>().Select(channel => new ReleaseChannelOption(Localization, channel)).ToArray();

    private ReleaseChannel SavedReleaseChannel => _services?.Settings.ReleaseChannel ?? ReleaseChannel.Stable;

    internal ReleaseChannelOption OptionFor(ReleaseChannel channel)
        => ReleaseChannelOptions.First(option => option.Channel == channel);

    internal string ReleaseStatusText(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Stable => Localization.ReleaseStable,
        ReleaseStatus.Testing => Localization.ReleaseTesting,
        ReleaseStatus.Dev => Localization.ReleaseDev,
        _ => Localization.ReleaseUnknown,
    };
}

/// <summary>One release channel, as the settings and the Show filter name it.</summary>
public sealed class ReleaseChannelOption : ObservableObject
{
    private readonly LocalizationService _localization;

    public ReleaseChannel Channel { get; }

    public string Text => Channel switch
    {
        ReleaseChannel.Testing => _localization.ReleaseChannelTesting,
        ReleaseChannel.Dev => _localization.ReleaseChannelDev,
        _ => _localization.ReleaseChannelStable,
    };

    public ReleaseChannelOption(LocalizationService localization, ReleaseChannel channel)
    {
        _localization = localization;
        Channel = channel;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(Text));
}
