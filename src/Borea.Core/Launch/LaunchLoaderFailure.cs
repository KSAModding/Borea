namespace Borea.Core.Launch;

public enum LaunchLoaderFailure
{
    /// <summary>A loader was chosen.</summary>
    None = 0,

    /// <summary>The loader id the user gave is not recorded as installed.</summary>
    GivenLoaderNotInstalled = 1,

    /// <summary>The mods of the instance need one loader, and it is not recorded as installed.</summary>
    NeededLoaderNotInstalled = 2,

    /// <summary>The mods of the instance need more than one loader.</summary>
    DifferentLoadersNeeded = 3,

    /// <summary>A loader is recorded as installed, and no live mod loader listing has its id.</summary>
    LoaderNotListed = 4,

    /// <summary>No mod needs a loader, every installed loader is listed, and no listing says how the loader takes an instance.</summary>
    NoLoaderTakesInstance = 5,
}
