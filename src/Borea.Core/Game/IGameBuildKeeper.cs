using Borea.Core.Instances;
using Borea.Core.Settings;

namespace Borea.Core.Game;

/// <summary>
/// Keeps the build in the game folder of the settings for an instance, as a copy in the Game Builds folder of the
/// library that the instance then always starts on.
/// </summary>
public interface IGameBuildKeeper
{
    /// <summary>What keeping the build would do for the instance, or why it is refused. It only reads.</summary>
    GameBuildKeepCheck Check(Instance instance);

    /// <summary>
    /// Copies the build once and sets it as the build of the instance. A refused check copies nothing and is
    /// returned as it is. A cancel or a failure removes the partial copy before the exception leaves.
    /// </summary>
    /// <param name="expected">The build the player confirmed. Another build in the game folder is refused as <see cref="GameBuildKeepRefusal.BuildChanged"/>.</param>
    Task<GameBuildKeepCheck> KeepAsync(Guid instanceId, GameVersion expected, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken);
}

/// <param name="Version">The build in the game folder of the settings, or the build the instance already keeps.</param>
/// <param name="Folder">The folder of that build in the Game Builds folder.</param>
/// <param name="BytesToCopy">0 when the folder already holds this build, so the instance uses that copy.</param>
/// <param name="FreeBytes">The free space where the copy goes, or null when it could not be read or nothing is copied.</param>
public sealed record GameBuildKeepCheck(GameBuildKeepRefusal Refusal, GameVersion? Version, string? Folder, long BytesToCopy, long? FreeBytes);

public enum GameBuildKeepRefusal
{
    None = 0,

    /// <summary>The instance already keeps a build.</summary>
    AlreadyKept = 1,

    /// <summary>The settings name no game folder, or its KSA.dll gives no version.</summary>
    NoGame = 2,

    /// <summary>The game is in a macOS Wine wrapper, which cannot start a copy outside its prefix.</summary>
    WineWrapper = 3,

    GameRunning = 4,

    /// <summary>The folder of the build exists and holds another build or none.</summary>
    FolderTaken = 5,

    /// <summary>The library folder is inside the game folder, so the copy would hold the library.</summary>
    LibraryInsideGame = 6,

    NotEnoughSpace = 7,

    /// <summary>The game folder holds another build than the one the player confirmed, for example after a client update.</summary>
    BuildChanged = 8,
}
