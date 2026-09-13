namespace Borea.Core.Launch;

public enum SharedProfileLaunchOutcome
{
    /// <summary>The game process is running.</summary>
    Started = 0,

    /// <summary>Borea does not know the name of the game's executable on this platform.</summary>
    UnknownExecutable = 1,

    /// <summary>No game directory is configured.</summary>
    NoGameDirectory = 2,

    /// <summary>The executable is not in the game directory.</summary>
    ExecutableMissing = 3,

    /// <summary>The operating system refused to start the process.</summary>
    StartFailed = 4,
}
