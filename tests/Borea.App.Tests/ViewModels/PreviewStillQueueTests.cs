using Borea.App.Tests.Views;
using Borea.App.ViewModels;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

[Collection(HeadlessCollection.Name)]
public sealed class PreviewStillQueueTests
{
    private static DiscoverItem Row(MainViewModel owner, string id) =>
        new(owner, new ModMetadata(1, id, "index", id, ["Maxi"], "A vehicle.", "MIT", new Dictionary<string, string>(), "2026.8.19.5261", type: ContentType.Vehicle));

    /// <summary>A draw that waits for its gate, and records its row, how many draws run at once and whether it was stopped.</summary>
    private sealed class Draws
    {
        private readonly Dictionary<string, TaskCompletionSource> _started = [];
        private readonly Dictionary<string, TaskCompletionSource> _gates = [];
        private int _running;

        public List<string> Started { get; } = [];

        public List<string> Stopped { get; } = [];

        public int MostAtOnce { get; private set; }

        /// <summary>Completes when the draw of the row runs. A row that never draws leaves the test to the time limit of its dispatch.</summary>
        public Task WhenStarted(string id) => Signal(_started, id).Task;

        public void Finish(string id) => Signal(_gates, id).TrySetResult();

        public async Task DrawAsync(DiscoverItem row, CancellationToken token)
        {
            Started.Add(row.ModId);
            MostAtOnce = Math.Max(MostAtOnce, ++_running);

            // a row that draws a second time waits for a gate of its own
            _gates.Remove(row.ModId);
            var gate = Signal(_gates, row.ModId);
            Signal(_started, row.ModId).TrySetResult();
            try
            {
                await gate.Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                Stopped.Add(row.ModId);
            }
            finally
            {
                _running--;
                _started.Remove(row.ModId);
            }
        }

        private static TaskCompletionSource Signal(Dictionary<string, TaskCompletionSource> signals, string id)
        {
            if (!signals.TryGetValue(id, out var signal))
                signals[id] = signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            return signal;
        }
    }

    [Fact]
    public async Task OneRowDrawsAtATime_TheNewestInViewFirst_ThenTheNewestNearTheView()
    {
        var (started, most) = await HeadlessApp.RunAsync(async () =>
        {
            var owner = new MainViewModel();
            var draws = new Draws();
            var queue = new PreviewStillQueue(draws.DrawAsync);
            foreach (var (id, inView) in new[] { ("InView1", true), ("InView2", true), ("Near1", false), ("Near2", false) })
                queue.Show(Row(owner, id), inView);

            foreach (var id in new[] { "InView2", "InView1", "Near2", "Near1" })
            {
                await draws.WhenStarted(id);
                draws.Finish(id);
            }

            await queue.Idle;
            return (draws.Started, draws.MostAtOnce);
        });

        Assert.Equal(["InView2", "InView1", "Near2", "Near1"], started);
        Assert.Equal(1, most);
    }

    [Fact]
    public async Task ARowThatLeft_IsDropped_AndItsRunningDrawStops()
    {
        var (started, stopped) = await HeadlessApp.RunAsync(async () =>
        {
            var owner = new MainViewModel();
            var draws = new Draws();
            var queue = new PreviewStillQueue(draws.DrawAsync);
            var running = Row(owner, "Running");
            var left = Row(owner, "Left");
            var stays = Row(owner, "Stays");
            queue.Show(running, inView: true);
            await draws.WhenStarted("Running");
            queue.Show(stays, inView: true);
            queue.Show(left, inView: true);

            queue.Hide(left);
            queue.Hide(running);

            await draws.WhenStarted("Stays");
            draws.Finish("Stays");
            await queue.Idle;
            return (draws.Started, draws.Stopped);
        });

        Assert.Equal(["Running", "Stays"], started);
        Assert.Equal(["Running"], stopped);
    }

    [Fact]
    public async Task Stop_EndsTheDrawAndForgetsTheRest_UntilARefreshAsksAgain()
    {
        var (afterStop, afterRefresh) = await HeadlessApp.RunAsync(async () =>
        {
            var owner = new MainViewModel();
            var draws = new Draws();
            var queue = new PreviewStillQueue(draws.DrawAsync);
            queue.Show(Row(owner, "First"), inView: true);
            await draws.WhenStarted("First");
            queue.Show(Row(owner, "Second"), inView: false);

            await queue.Stop();
            var stopped = draws.Started.ToList();

            queue.Refresh();
            await draws.WhenStarted("First");
            draws.Finish("First");
            await draws.WhenStarted("Second");
            draws.Finish("Second");
            await queue.Idle;
            return (stopped, draws.Started.Skip(stopped.Count).ToList());
        });

        Assert.Equal(["First"], afterStop);
        Assert.Equal(["First", "Second"], afterRefresh);
    }
}
