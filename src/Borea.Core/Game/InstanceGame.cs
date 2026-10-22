namespace Borea.Core.Game;

/// <summary>
/// The game an instance starts. Every instance starts the game folder of the settings today, so
/// <see cref="KeptBuild"/> is always null and <see cref="Problem"/> is always <see cref="KeptBuildProblem.None"/>.
/// </summary>
public sealed record InstanceGame(GameVersion? KeptBuild, string? GameDirectory, InstalledGameVersion? Version, KeptBuildProblem Problem);

public enum KeptBuildProblem
{
    None = 0,
}
