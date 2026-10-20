using Borea.Core.Mods;

namespace Borea.Core.Updates;

/// <summary>Which Borea releases the update check reports. It does not change which mod releases Borea installs.</summary>
public enum BoreaUpdateChannel
{
    /// <summary>Only releases that are not pre-releases. The default.</summary>
    Stable = 0,

    /// <summary>Stable releases and pre-releases tagged "-beta".</summary>
    Testing = 1,

    /// <summary>Every release, pre-releases of any name included, except the preview builds of a feature branch.</summary>
    Dev = 2,
}

public static class BoreaUpdateChannels
{
    /// <summary>Whether <paramref name="channel"/> reports a release of <paramref name="version"/>.</summary>
    public static bool Includes(this BoreaUpdateChannel channel, ModVersion version) => channel switch
    {
        // A preview build comes from a feature branch, so no channel offers it and its testers download it by hand.
        BoreaUpdateChannel.Dev => version.PreRelease is null || !Starts(version.PreRelease, "preview"),
        BoreaUpdateChannel.Testing => version.PreRelease is null || Starts(version.PreRelease, "beta"),
        _ => version.PreRelease is null,
    };

    private static bool Starts(string preRelease, string label)
        => string.Equals(preRelease.Split('.')[0], label, StringComparison.OrdinalIgnoreCase);
}
