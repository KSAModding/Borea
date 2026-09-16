namespace Borea.Core.Instances;

/// <summary>
/// Adds up how long an instance was played from the session logs the game writes in it, which also cover a start without Borea.
/// </summary>
public interface IPlaytimeService
{
    Task<InstancePlaytime> GetPlaytimeAsync(Guid instanceId, CancellationToken cancellationToken = default);
}

/// <param name="Sessions">How many sessions lasted at least <see cref="GameSession.MinimumLength"/>.</param>
/// <param name="UnreadableLogs">How many logs have lines but none with a time that Borea can read.</param>
/// <param name="IsKnown">False when the newest log is unreadable, because the total then likely misses every session since the log format changed.</param>
public sealed record InstancePlaytime(TimeSpan Total, int Sessions, bool IncludesRunningSession, int UnreadableLogs, bool IsKnown);
