using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Borea.App.SingleInstance;

/// <summary>
/// Takes the starts that later Borea processes hand over, while this App holds the lock.
/// Everything a client sends is untrusted, so each connection has a time limit and a
/// message that does not decode is rejected.
/// </summary>
internal sealed class HandoverServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Func<IReadOnlyList<string>, bool> _onStart;
    private readonly Action<string> _log;
    private readonly TimeSpan _exchangeTimeout;
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<Task> _connections = [];
    private readonly Task _acceptLoop;
    private readonly Lazy<Task> _stopped;

    private HandoverServer(string pipeName, Func<IReadOnlyList<string>, bool> onStart, Action<string> log, TimeSpan exchangeTimeout)
    {
        _pipeName = pipeName;
        _onStart = onStart;
        _log = log;
        _exchangeTimeout = exchangeTimeout;
        _stopped = new Lazy<Task>(StopAsync);
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public static HandoverServer Start(string pipeName, Func<IReadOnlyList<string>, bool> onStart, Action<string> log, TimeSpan exchangeTimeout)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(onStart);
        ArgumentNullException.ThrowIfNull(log);
        return new HandoverServer(pipeName, onStart, log, exchangeTimeout);
    }

    public static NamedPipeServerStream CreatePipe(string pipeName) =>
        new(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(_pipeName);
            }
            catch (Exception exception)
            {
                _log($"Borea cannot take starts from other Borea processes: {exception.Message}");
                return;
            }

            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A client that leaves at once, or on Linux and macOS a client of another user, breaks only its own connection.
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            var connection = ServeAsync(pipe);
            lock (_connections)
                _connections.Add(connection);
            _ = connection.ContinueWith(done => { lock (_connections) _connections.Remove(done); }, TaskScheduler.Default);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        await using var _ = pipe.ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(_exchangeTimeout);
        try
        {
            await HandoverProtocol.WriteAsync(pipe, HandoverProtocol.EncodeHello(Environment.ProcessId), timeout.Token).ConfigureAwait(false);
            var arguments = HandoverProtocol.DecodeStart(await HandoverProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false));
            var answer = Take(arguments) ? HandoverProtocol.EncodeAccepted() : HandoverProtocol.EncodeRejected();
            await HandoverProtocol.WriteAsync(pipe, answer, timeout.Token).ConfigureAwait(false);
            await WaitForClientToCloseAsync(pipe, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            _log("Dropped a start from another Borea process because it timed out.");
        }
        catch (Exception exception)
        {
            _log($"Dropped a start from another Borea process: {exception.Message}");
        }
    }

    private bool Take(IReadOnlyList<string>? arguments)
    {
        if (arguments is null)
        {
            _log("Rejected a start from another Borea process because its message was not valid.");
            return false;
        }

        if (!_onStart(arguments))
        {
            _log("Rejected a start from another Borea process because too many starts wait for the window.");
            return false;
        }

        _log($"Took a start from another Borea process with {arguments.Count} arguments.");
        return true;
    }

    // Closing before the client read the answer can discard the answer.
    private static async Task WaitForClientToCloseAsync(Stream pipe, CancellationToken cancellationToken)
    {
        try
        {
            await pipe.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
        }
    }

    public ValueTask DisposeAsync() => new(_stopped.Value);

    private async Task StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _acceptLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        Task[] connections;
        lock (_connections)
            connections = [.. _connections];
        await Task.WhenAll(connections).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _stop.Dispose();
    }
}
