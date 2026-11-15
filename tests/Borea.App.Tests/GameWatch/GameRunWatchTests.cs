using Borea.App.GameWatch;

namespace Borea.App.Tests.GameWatch;

public sealed class GameRunWatchTests
{
    /// <summary>The game runs from <paramref name="from"/> to <paramref name="until"/> seconds, and the watch polls every 3 seconds.</summary>
    [Theory]
    [InlineData(6, 15, true, 15)]
    [InlineData(120, 130, false, 90)]
    public async Task WaitForEndAsync_GameRunsOrNever_EndsAtTheFirstPollAfterItOrAtTheLimit(int from, int until, bool expected, int endsAt)
    {
        var clock = new AdvancingClock();
        var watch = new GameRunWatch(() => clock.Elapsed >= TimeSpan.FromSeconds(from) && clock.Elapsed < TimeSpan.FromSeconds(until), clock);

        var ended = await watch.WaitForEndAsync();

        Assert.Equal(expected, ended);
        Assert.Equal(TimeSpan.FromSeconds(endsAt), clock.Elapsed);
    }
}
