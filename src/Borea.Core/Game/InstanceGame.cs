namespace Borea.Core.Game;

/// <summary>
/// The game an instance starts. <see cref="KeptBuild"/> is the build the instance names, or null when it runs on the
/// game folder of the settings. When the kept build has a problem, the folder and the version are null, so a caller
/// never falls back to another game.
/// </summary>
public sealed record InstanceGame(GameVersion? KeptBuild, string? GameDirectory, InstalledGameVersion? Version, KeptBuildProblem Problem);

public enum KeptBuildProblem
{
    None = 0,

    /// <summary>The folder of the kept build is gone.</summary>
    Missing = 1,

    /// <summary>The KSA.dll in the folder of the kept build is missing or reads another version.</summary>
    WrongVersion = 2,
}
