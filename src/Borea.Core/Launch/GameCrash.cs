namespace Borea.Core.Launch;

/// <summary>A crash the game recorded in the logs folder of an instance.</summary>
/// <param name="LogPath">The abnormal-exit or previous-crash log of the run.</param>
/// <param name="Run">The name of the run's log without its extension, the same for both logs of one crash.</param>
/// <param name="WrittenAt">When the game wrote the crash log.</param>
/// <param name="Exception">The type and message of the exception that ended the game, or null when the log holds none.</param>
/// <param name="BlamedModId">The installed mod whose assembly the exception names, or null.</param>
/// <param name="AfterShutdown">True when the error came from the mod loader's clean up after the game had shut down, so the player's session ended normally.</param>
public sealed record GameCrash(string LogPath, string Run, DateTimeOffset WrittenAt, string? Exception, string? BlamedModId, bool AfterShutdown = false);
