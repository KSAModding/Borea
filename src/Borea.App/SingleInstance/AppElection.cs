using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Borea.App.SingleInstance;

internal sealed record HandoverTimeouts(TimeSpan Connect, TimeSpan Exchange, TimeSpan Deadline)
{
    public static HandoverTimeouts Default { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
}

internal sealed class PrimaryInstance(AppLock appLock, HandoverServer server) : IDisposable
{
    // A later start then waits for the lock instead of reaching a window that is about to close.
    public Task StopTakingStartsAsync() => server.DisposeAsync().AsTask();

    public void Dispose()
    {
        StopTakingStartsAsync().GetAwaiter().GetResult();
        appLock.Dispose();
    }
}

/// <summary>Exactly one of the three is set.</summary>
internal sealed record ElectionResult(PrimaryInstance? Primary, int? HandedToProcessId, string? Failure);

/// <summary>Decides whether this process opens the App or hands its start to the App that holds the lock.</summary>
internal static class AppElection
{
    public const int HandoverFailedExitCode = 3;

    // sun_path holds 104 bytes on macOS and 108 on Linux, with the closing zero.
    private const int MaxSocketPathBytes = 100;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Tries again until the deadline, because the holder of the lock can be starting up or shutting down.</summary>
    /// <param name="onStart">Called on a pool thread with each start that a later process hands over. False rejects it.</param>
    public static async Task<ElectionResult> RunAsync(
        string lockPath,
        IReadOnlyList<string> arguments,
        Func<IReadOnlyList<string>, bool> onStart,
        Action<string> log,
        HandoverTimeouts timeouts,
        CancellationToken cancellationToken = default)
    {
        byte[] startMessage;
        try
        {
            startMessage = HandoverProtocol.EncodeStart(arguments);
        }
        catch (ArgumentException exception)
        {
            return new ElectionResult(null, null, exception.Message);
        }

        var pipeName = PipeName(lockPath);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var appLock = AppLock.TryAcquire(lockPath, out var lockError);
            if (appLock is not null)
                return new ElectionResult(new PrimaryInstance(appLock, HandoverServer.Start(pipeName, onStart, log, timeouts.Exchange)), null, null);

            string failure;
            try
            {
                var processId = await HandoverClient.HandOverAsync(pipeName, startMessage, timeouts, cancellationToken).ConfigureAwait(false);
                return new ElectionResult(null, processId, null);
            }
            catch (HandoverRejectedException exception)
            {
                return new ElectionResult(null, null, exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                // The pipe belongs to another user, or on Windows to the same user with or without administrator rights.
                return new ElectionResult(null, null, exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failure = lockError is IOException ? exception.Message : $"{exception.Message} The lock file cannot be opened: {lockError?.Message}";
            }

            if (elapsed.Elapsed + RetryDelay >= timeouts.Deadline)
                return new ElectionResult(null, null, failure);

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string PipeName(string lockPath) =>
        PipeName(lockPath, OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));

    /// <summary>
    /// On Linux and macOS the pipe is a socket file. A rooted name puts it in a folder of this user,
    /// because the temp folder is shared by all users and can differ between two starts.
    /// </summary>
    internal static string PipeName(string lockPath, bool windows, string? runtimeDirectory)
    {
        var fullPath = Path.GetFullPath(lockPath);
        if (windows)
            fullPath = fullPath.ToUpperInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.UserName}\n{fullPath}"));
        var name = "Borea-" + Convert.ToHexStringLower(hash.AsSpan(0, 12));
        if (windows)
            return name;

        var nextToLock = Path.Combine(Path.GetDirectoryName(fullPath)!, "app.sock");
        if (Encoding.UTF8.GetByteCount(nextToLock) <= MaxSocketPathBytes)
            return nextToLock;

        if (!string.IsNullOrEmpty(runtimeDirectory) && Path.IsPathRooted(runtimeDirectory))
        {
            var inRuntimeDirectory = Path.Combine(runtimeDirectory, name + ".sock");
            if (Encoding.UTF8.GetByteCount(inRuntimeDirectory) <= MaxSocketPathBytes)
                return inRuntimeDirectory;
        }

        return name;
    }
}
