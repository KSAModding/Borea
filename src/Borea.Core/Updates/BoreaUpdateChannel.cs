using Borea.Core.Mods;

namespace Borea.Core.Updates;

/// <summary>Which Borea releases the update check reports. It does not change which mod releases Borea installs.</summary>
public enum BoreaUpdateChannel
{
    /// <summary>Only releases that are not pre-releases. The default.</summary>
    Stable = 0,

    /// <summary>Stable releases and pre-releases tagged "-beta".</summary>
    Testing = 1,

    /// <summary>Every release, pre-releases of any name included.</summary>
    Dev = 2,
}

public static class BoreaUpdateChannels
{
    /// <summary>Whether <paramref name="channel"/> reports a release of <paramref name="version"/>.</summary>
    public static bool Includes(this BoreaUpdateChannel channel, ModVersion version) => channel switch
    {
        BoreaUpdateChannel.Dev => true,
        BoreaUpdateChannel.Testing => version.PreRelease is null || IsBeta(version.PreRelease),
        _ => version.PreRelease is null,
    };

    private static bool IsBeta(string preRelease)
        => string.Equals(preRelease.Split('.')[0], "beta", StringComparison.OrdinalIgnoreCase);
}
