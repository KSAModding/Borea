namespace Borea.Core.Launch;

public enum LaunchOutcome
{
    /// <summary>The loader process is running.</summary>
    Started = 0,

    /// <summary>No loader was given, and the game cannot be started for an instance without one.</summary>
    NoLoader = 1,

    /// <summary>The loader's listing does not name what to run.</summary>
    NoLaunchTarget = 2,

    /// <summary>The loader's listing does not say how the loader takes an instance.</summary>
    NoInstanceHandover = 3,

    /// <summary>No directory is configured for the loader.</summary>
    NoLoaderDirectory = 4,

    /// <summary>The executable the listing names is not in the loader directory.</summary>
    LaunchTargetMissing = 5,

    /// <summary>A launch this launcher started for the instance is still running.</summary>
    AlreadyRunning = 6,

    /// <summary>The operating system refused to start the process.</summary>
    StartFailed = 7,

    /// <summary>The loader runs through dotnet on this system, and no dotnet host was found.</summary>
    DotnetMissing = 8,

    /// <summary>The loader started, then stopped with an error before the game came up.</summary>
    ExitedEarly = 9,

    /// <summary>The launch arguments contain the flag the loader reads the instance root from.</summary>
    HandoverFlagInArguments = 10,
}
