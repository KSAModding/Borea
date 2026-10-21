using Borea.Core.Instances;
using Borea.Core.Settings;

namespace Borea.Core.Game;

/// <summary>
/// Keeps the build in the game folder of the settings for an instance, or the build in a folder the player chose,
/// as a copy in the Game Builds folder of the library that instances can start on.
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

    /// <summary>
    /// Clears the build of the instance, so it starts on the game folder of the settings again. The saves stay in the
    /// instance, and the kept build stays in the Game Builds folder.
    /// </summary>
    Task<GameBuildChangeRefusal> UseCurrentBuildAsync(Guid instanceId, CancellationToken cancellationToken);

    /// <summary>The kept builds in the Game Builds folder, newest first, each with its size and the instances that use it.</summary>
    Task<IReadOnlyList<KeptGameBuild>> ListKeptAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the folder of a kept build that no instance uses and that is not the game folder of the settings.
    /// Before that, a loader whose game path names the folder gets the game folder of the settings.
    /// </summary>
    Task<GameBuildChangeRefusal> RemoveAsync(GameVersion build, CancellationToken cancellationToken);

    /// <summary>What keeping the build in <paramref name="folder"/> would do, or why it is refused. It only reads.</summary>
    GameBuildKeepCheck CheckFolder(string folder);

    /// <summary>
    /// Copies the build in <paramref name="folder"/> once, for new instances to pick, and sets the build of no
    /// instance. Refusals, cancels and failures work as in <see cref="KeepAsync"/>.
    /// </summary>
    /// <param name="expected">The build the player confirmed. Another build in the folder is refused as <see cref="GameBuildKeepRefusal.BuildChanged"/>.</param>
    Task<GameBuildKeepCheck> KeepFolderAsync(string folder, GameVersion expected, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken);

    /// <summary>The kept builds whose KSA.dll reads the build their folder names, newest first.</summary>
    IReadOnlyList<GameVersion> ListKept();
}

/// <param name="InstanceNames">The names of the instances that keep this build, empty when no instance keeps it.</param>
/// <param name="IsGameFolderOfSettings">Whether the game folder of the settings is this folder, so every instance without a kept build starts on it too.</param>
public sealed record KeptGameBuild(GameVersion Version, string Folder, long Bytes, IReadOnlyList<string> InstanceNames, bool IsGameFolderOfSettings);

public enum GameBuildChangeRefusal
{
    None = 0,

    GameRunning = 1,

    /// <summary>An instance starts on the build.</summary>
    InUse = 2,

    /// <summary>An instance record cannot be read, so it could use the build.</summary>
    InstanceUnreadable = 3,

    /// <summary>The game folder of the settings is the folder of the build.</summary>
    GameFolderOfSettings = 4,

    /// <summary>Another keep, clear or remove holds the Game Builds folder, and a keep can copy for minutes.</summary>
    Busy = 5,
}

/// <param name="Version">The build in the game folder of the settings or in the folder the player chose, or the build the instance already keeps.</param>
/// <param name="Folder">The folder of that build in the Game Builds folder.</param>
/// <param name="BytesToCopy">0 when the folder already holds this build, so nothing is copied.</param>
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

    /// <summary>The source folder holds another build than the one the player confirmed, for example after a client update.</summary>
    BuildChanged = 8,

    /// <summary>
    /// The folder the player chose holds no executable that this system starts, KSA.exe on Windows and the app host
    /// KSA on Linux, or its KSA.dll gives no version. On macOS every folder is refused.
    /// </summary>
    NotAGame = 9,
}
