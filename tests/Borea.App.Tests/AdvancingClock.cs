namespace Borea.App.Tests;

/// <summary>
/// A clock whose timers fire at once on the thread pool and move the time forward by their due time,
/// so a loop that polls every few seconds runs through minutes without a wait.
/// </summary>
internal sealed class AdvancingClock : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now = Start;

    internal static readonly DateTimeOffset Start = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    internal TimeSpan Elapsed => GetUtcNow() - Start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_gate)
                _now += dueTime;
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return System.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
}
