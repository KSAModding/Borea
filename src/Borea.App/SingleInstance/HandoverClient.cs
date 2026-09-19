using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Borea.App.SingleInstance;

/// <summary>The running App answered, but it cannot take this start, so trying again does not help.</summary>
internal sealed class HandoverRejectedException(string message) : Exception(message);

internal static class HandoverClient
{
    /// <returns>The process id of the running App.</returns>
    public static async Task<int> HandOverAsync(string pipeName, byte[] startMessage, HandoverTimeouts timeouts, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeouts.Connect, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("No running Borea App answered.");
        }

        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        exchange.CancelAfter(timeouts.Exchange);
        try
        {
            var processId = HandoverProtocol.DecodeHello(await HandoverProtocol.ReadAsync(pipe, exchange.Token).ConfigureAwait(false))
                ?? throw new HandoverRejectedException("The running Borea App sent a hello that this version cannot read.");

            AllowForeground(processId);
            await HandoverProtocol.WriteAsync(pipe, startMessage, exchange.Token).ConfigureAwait(false);
            if (!HandoverProtocol.IsAccepted(await HandoverProtocol.ReadAsync(pipe, exchange.Token).ConfigureAwait(false)))
                throw new HandoverRejectedException("The running Borea App rejected the start.");

            return processId;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The running Borea App did not answer in time.");
        }
        catch (InvalidDataException exception)
        {
            throw new HandoverRejectedException(exception.Message);
        }
    }

    // Windows gives the foreground to the process the user just started, so this
    // process passes that right on before the running App activates its window.
    private static void AllowForeground(int processId)
    {
        if (OperatingSystem.IsWindows() && processId != Environment.ProcessId)
            AllowSetForegroundWindow((uint)processId);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
