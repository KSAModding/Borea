namespace Borea.Core.Instances;

/// <summary>
/// One run of the game, from the first line of its log that has a time to the last one.
/// </summary>
public sealed record GameSession(DateTimeOffset Start, DateTimeOffset End, bool ClosedCleanly)
{
    /// <summary>A shorter session is a start that failed, so it adds no playtime and is not counted.</summary>
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(1);

    public TimeSpan Length => End - Start;

    public bool Counts => Length >= MinimumLength;
}
