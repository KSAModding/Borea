namespace Borea.Core.Instances;

/// <summary>
/// Reads the end of the log the game writes in an instance.
/// </summary>
public interface IGameLogReader
{
    Task<GameLogTail> ReadGameLogAsync(Guid instanceId, CancellationToken cancellationToken = default);
}

/// <param name="Path">Where the game writes its log for the instance.</param>
/// <param name="Exists">Whether the game wrote a log there.</param>
/// <param name="Lines">The last lines of the log, oldest first. Empty when there is no log.</param>
public sealed record GameLogTail(string Path, bool Exists, IReadOnlyList<string> Lines);
