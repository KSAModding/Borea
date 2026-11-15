namespace Borea.App.Tests.ViewModels;

public sealed class MemoryReleaseTests
{
    [Fact]
    public async Task ReleaseMemory_RunsOnceAfterTheStartWorkAndTheGraceAndNotBefore()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var startDone = false;
        var clock = new GraceClock();
        var releases = 0;
        var released = new TaskCompletionSource();
        var viewModel = harness.NewViewModel(services: harness.Services);
        viewModel.PropertyChanged += (_, e) =>
        {
            // the last step of the start raises this
            if (e.PropertyName == "ShowSharedProfileBanner")
                startDone = true;
        };
        clock.OnTimer = () => Assert.True(startDone);
        viewModel.Clock = clock;
        viewModel.ReleaseMemory = () =>
        {
            releases++;
            released.SetResult();
        };

        await viewModel.LoadAsync();
        Assert.True(clock.HasTimer);
        Assert.Equal(0, releases);

        clock.Fire();
        await released.Task;
        Assert.Equal(1, releases);

        await viewModel.LoadAsync();
        Assert.Equal(1, releases);
    }

    private sealed class GraceClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;

        public Action? OnTimer { get; set; }

        public bool HasTimer => _callback is not null;

        public void Fire() => _callback!(_state);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
                return base.CreateTimer(callback, state, dueTime, period);

            OnTimer?.Invoke();
            _callback = callback;
            _state = state;
            return base.CreateTimer(static _ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }
}
