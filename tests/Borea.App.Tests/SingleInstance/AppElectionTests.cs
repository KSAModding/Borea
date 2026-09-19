using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Borea.App.SingleInstance;

namespace Borea.App.Tests.SingleInstance;

public sealed class AppElectionTests : IDisposable
{
    private static readonly HandoverTimeouts Generous = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

    private static readonly HandoverTimeouts Short = new(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaSingleInstance_" + Guid.NewGuid());

    private readonly ConcurrentQueue<IReadOnlyList<string>> _received = new();

    private readonly ConcurrentQueue<string> _log = new();

    private string LockPath => Path.Combine(_root, "app.lock");

    // On Linux the socket sits next to the lock, and a socket cannot be created in a missing folder.
    public AppElectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task FirstStart_IsPrimary_SecondStart_HandsOverItsArguments()
    {
        var first = await RunAsync([], Generous);
        using var primary = Assert.IsType<PrimaryInstance>(first.Primary);

        var second = await RunAsync(["borea://mod/Example"], Generous);

        Assert.Null(second.Primary);
        Assert.Equal(Environment.ProcessId, second.HandedToProcessId);
        Assert.Equal(["borea://mod/Example"], Assert.Single(_received));
        Assert.Contains(_log, line => line.Contains("Took a start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TwoStartsAtOnce_ElectExactlyOnePrimary()
    {
        var results = await Task.WhenAll(
            Task.Run(() => RunAsync(["a"], Generous)),
            Task.Run(() => RunAsync(["b"], Generous)));

        var primary = Assert.Single(results, result => result.Primary is not null);
        using var _ = primary.Primary;
        var handedOver = Assert.Single(results, result => result.HandedToProcessId is not null);
        Assert.Equal(Environment.ProcessId, handedOver.HandedToProcessId);
        Assert.Single(_received);
    }

    [Fact]
    public async Task PrimaryEnds_NextStartBecomesPrimary()
    {
        var first = await RunAsync([], Generous);
        first.Primary!.Dispose();

        var next = await RunAsync([], Generous);

        using var primary = Assert.IsType<PrimaryInstance>(next.Primary);
    }

    [Fact]
    public async Task PrimaryStopsTakingStarts_NextStartWaitsForTheLock()
    {
        var first = (await RunAsync([], Generous)).Primary!;
        await first.StopTakingStartsAsync();

        var next = RunAsync([], Generous);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(next.IsCompleted);
        first.Dispose();

        using var primary = Assert.IsType<PrimaryInstance>((await next).Primary);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task LockHeldWithoutAnswer_FailsBeforeTheDeadline()
    {
        using var held = AppLock.TryAcquire(LockPath, out _);
        Assert.NotNull(held);
        var elapsed = Stopwatch.StartNew();

        var result = await RunAsync([], Short);

        Assert.Null(result.Primary);
        Assert.Null(result.HandedToProcessId);
        Assert.False(string.IsNullOrEmpty(result.Failure));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"The election took {elapsed.Elapsed}.");
    }

    [Fact]
    public async Task ServerThatNeverAnswers_TimesOut()
    {
        var pipeName = AppElection.PipeName(LockPath);
        await using var silent = HandoverServer.CreatePipe(pipeName);
        var connected = silent.WaitForConnectionAsync();
        var elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() => HandoverClient.HandOverAsync(pipeName, HandoverProtocol.EncodeStart([]), Short, CancellationToken.None));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"The handover took {elapsed.Elapsed}.");
        await connected;
    }

    [Fact]
    public async Task RunningAppOfAnotherVersion_FailsWithoutWaitingForTheDeadline()
    {
        using var held = AppLock.TryAcquire(LockPath, out _);
        Assert.NotNull(held);
        await using var other = HandoverServer.CreatePipe(AppElection.PipeName(LockPath));
        var answered = AnswerWithAsync(other, """{"version":2,"type":"hello","processId":1}""");
        var elapsed = Stopwatch.StartNew();

        var result = await RunAsync([], Generous);

        Assert.Null(result.HandedToProcessId);
        Assert.Contains("cannot read", result.Failure, StringComparison.Ordinal);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3), $"The election took {elapsed.Elapsed}.");
        await answered;
    }

    [Fact]
    public async Task StartOfAnotherVersion_IsRejectedOnce()
    {
        var pipeName = AppElection.PipeName(LockPath);
        await using var server = HandoverServer.Start(pipeName, Take, _log.Enqueue, TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<HandoverRejectedException>(() => HandoverClient.HandOverAsync(
            pipeName, Encoding.UTF8.GetBytes("""{"version":2,"type":"start","arguments":[]}"""), Generous, CancellationToken.None));

        Assert.Empty(_received);
        Assert.Single(_log, line => line.Contains("not valid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartThatTheAppCannotHold_IsRejected()
    {
        var pipeName = AppElection.PipeName(LockPath);
        await using var server = HandoverServer.Start(pipeName, _ => false, _log.Enqueue, TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<HandoverRejectedException>(() => HandoverClient.HandOverAsync(
            pipeName, HandoverProtocol.EncodeStart(["borea://mod/Example"]), Generous, CancellationToken.None));

        Assert.Contains(_log, line => line.Contains("too many starts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartThatThrows_IsDropped_AndTheNextStartStillArrives()
    {
        var pipeName = AppElection.PipeName(LockPath);
        var calls = 0;
        await using var server = HandoverServer.Start(
            pipeName,
            arguments => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("boom") : Take(arguments),
            _log.Enqueue,
            TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<EndOfStreamException>(() => HandoverClient.HandOverAsync(
            pipeName, HandoverProtocol.EncodeStart(["first"]), Generous, CancellationToken.None));
        await HandoverClient.HandOverAsync(pipeName, HandoverProtocol.EncodeStart(["second"]), Generous, CancellationToken.None);

        Assert.Equal(["second"], Assert.Single(_received));
        Assert.Contains(_log, line => line.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClientThatSendsNothing_IsDropped_AndTheNextStartStillArrives()
    {
        var pipeName = AppElection.PipeName(LockPath);
        await using (var server = HandoverServer.Start(pipeName, Take, _log.Enqueue, TimeSpan.FromMilliseconds(300)))
        {
            await using var idle = await ConnectAsync(pipeName);
            await HandoverProtocol.ReadAsync(idle, CancellationToken.None);

            var processId = await HandoverClient.HandOverAsync(pipeName, HandoverProtocol.EncodeStart(["next"]), Generous, CancellationToken.None);

            Assert.Equal(Environment.ProcessId, processId);
            await WaitUntilAsync(() => _log.Any(line => line.Contains("timed out", StringComparison.Ordinal)));
        }

        Assert.Equal(["next"], Assert.Single(_received));
    }

    [Fact]
    public async Task MessageThatIsNotValid_IsDropped()
    {
        var pipeName = AppElection.PipeName(LockPath);
        await using var server = HandoverServer.Start(pipeName, Take, _log.Enqueue, TimeSpan.FromSeconds(5));
        await using var client = await ConnectAsync(pipeName);
        await HandoverProtocol.ReadAsync(client, CancellationToken.None);

        await HandoverProtocol.WriteAsync(client, Encoding.UTF8.GetBytes("""{"version":1,"type":"start","arguments":[1]}"""), CancellationToken.None);

        Assert.False(HandoverProtocol.IsAccepted(await HandoverProtocol.ReadAsync(client, CancellationToken.None)));
        Assert.Empty(_received);
        Assert.Contains(_log, line => line.Contains("not valid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OversizedLength_IsDropped()
    {
        var pipeName = AppElection.PipeName(LockPath);
        await using var server = HandoverServer.Start(pipeName, Take, _log.Enqueue, TimeSpan.FromSeconds(5));
        await using var client = await ConnectAsync(pipeName);
        await HandoverProtocol.ReadAsync(client, CancellationToken.None);

        await client.WriteAsync(BitConverter.GetBytes(int.MaxValue));

        await Assert.ThrowsAsync<EndOfStreamException>(() => HandoverProtocol.ReadAsync(client, CancellationToken.None));
        Assert.Empty(_received);
    }

    [Fact]
    public async Task ArgumentsOverTheCap_FailWithoutContactingTheApp()
    {
        var result = await RunAsync(Enumerable.Repeat("a", HandoverProtocol.MaxArguments + 1).ToArray(), Short);

        Assert.Null(result.Primary);
        Assert.NotNull(result.Failure);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public void PipeName_OnWindows_DependsOnlyOnTheLockPath_AndStaysShort()
    {
        var name = AppElection.PipeName(LockPath, windows: true, runtimeDirectory: null);

        Assert.Equal(name, AppElection.PipeName(LockPath.ToUpperInvariant(), windows: true, runtimeDirectory: null));
        Assert.NotEqual(name, AppElection.PipeName(Path.Combine(_root, "other", "app.lock"), windows: true, runtimeDirectory: null));
        Assert.False(Path.IsPathRooted(name));
        Assert.True(name.Length <= 30);
    }

    [Fact]
    public void PipeName_OnUnix_IsASocketNextToTheLock()
    {
        var lockPath = Path.GetFullPath("/home/kitten/.local/share/Borea/app.lock");

        var name = AppElection.PipeName(lockPath, windows: false, runtimeDirectory: "/run/user/1000");

        Assert.Equal(Path.Combine(Path.GetDirectoryName(lockPath)!, "app.sock"), name);
    }

    [Fact]
    public void PipeName_OnUnix_WithALongRoot_UsesTheRuntimeFolder_ThenTheTempFolder()
    {
        var lockPath = Path.GetFullPath("/" + new string('a', 120) + "/app.lock");

        var inRuntime = AppElection.PipeName(lockPath, windows: false, runtimeDirectory: "/run/user/1000");
        var inTemp = AppElection.PipeName(lockPath, windows: false, runtimeDirectory: null);

        Assert.StartsWith("/run/user/1000", inRuntime, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(inRuntime) <= 100);
        Assert.False(Path.IsPathRooted(inTemp));
        Assert.True(inTemp.Length <= 30);
    }

    private bool Take(IReadOnlyList<string> arguments)
    {
        _received.Enqueue(arguments);
        return true;
    }

    private Task<ElectionResult> RunAsync(string[] arguments, HandoverTimeouts timeouts) =>
        AppElection.RunAsync(LockPath, arguments, Take, _log.Enqueue, timeouts);

    private static async Task AnswerWithAsync(NamedPipeServerStream pipe, string hello)
    {
        await pipe.WaitForConnectionAsync();
        await HandoverProtocol.WriteAsync(pipe, Encoding.UTF8.GetBytes(hello), CancellationToken.None);
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName)
    {
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        return client;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), "The condition did not become true in time.");
            await Task.Delay(20);
        }
    }
}
