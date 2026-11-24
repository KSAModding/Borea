using System.Diagnostics;
using Borea.Core.Launch;

namespace Borea.Storage.Launch;

/// <summary>
/// Starts a plan as an operating system process with Borea's environment plus the
/// plan's variables. The process gets none of Borea's standard handles, so a caller
/// that pipes Borea's output does not wait for the game. Its own output and error
/// streams are read into a bounded buffer, so an error it writes before it stops can
/// be shown, and a full pipe never blocks it.
/// </summary>
public sealed class ProcessStarter : IProcessStarter
{
    /// <summary>How many of the latest output lines a started process keeps.</summary>
    public const int OutputLines = 200;

    // Handle inheritance is process-wide, so starts must not overlap.
    private static readonly object StartGate = new();

    public IStartedProcess Start(LaunchPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        // UseShellExecute off, so the environment and the argument list reach the process.
        // The streams are pipes Borea reads (input is closed at once), and a console loader opens no window.
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.Executable,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in plan.Arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var (name, value) in plan.EnvironmentVariables)
            startInfo.Environment[name] = value;

        Process process;
        lock (StartGate)
        {
            using (StandardHandleInheritance.Suspend())
            {
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"No process was started for '{plan.Executable}'.");
            }
        }

        process.StandardInput.Close();
        return new StartedProcess(process);
    }

    /// <summary>
    /// The start time of each system has its own precision, and Linux derives it from the boot time on every read, so
    /// two reads of one process can differ a little.
    /// </summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    public IStartedProcess? Find(int processId, DateTime startedAt)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // no process runs with this id
            return null;
        }

        if (!HasExitedSafely(process)
            && StartTimeOf(process) is { } start
            && (start - startedAt.ToUniversalTime()).Duration() <= StartTimeTolerance)
        {
            return new FoundProcess(process, start);
        }

        process.Dispose();
        return null;
    }

    private static bool HasExitedSafely(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // a process that Borea may not read is not one that Borea started
            return true;
        }
    }

    private static DateTime? StartTimeOf(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A process that an earlier Borea started. Its output went to that Borea, so this one reads none, and its end is
    /// the exit of the process alone.
    /// </summary>
    private sealed class FoundProcess : IStartedProcess
    {
        private readonly Process _process;
        private volatile bool _disposed;
        private bool _exitedAtRelease;

        public FoundProcess(Process process, DateTime startedAt)
        {
            _process = process;
            Id = process.Id;
            StartedAt = startedAt;
        }

        public int Id { get; }

        public DateTime? StartedAt { get; }

        public bool HasExited => _disposed ? _exitedAtRelease : HasExitedSafely(_process);

        // the exit code of a process that Borea did not start is not always readable, and no answer depends on it
        public int? ExitCode => null;

        public IReadOnlyList<string> RecentOutput => [];

        public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(timeout);
            try
            {
                await _process.WaitForExitAsync(window.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _exitedAtRelease = HasExitedSafely(_process);
            _disposed = true;
            _process.Dispose();
        }
    }

    private sealed class StartedProcess : IStartedProcess
    {
        private readonly Process _process;
        private readonly Queue<string> _output = new();
        private readonly object _outputGate = new();
        private readonly CancellationTokenSource _released = new();
        private bool _outputClosed;
        private bool _errorClosed;
        private volatile bool _disposed;
        private int? _exitCodeAtRelease;

        public StartedProcess(Process process)
        {
            _process = process;
            Id = process.Id;
            StartedAt = StartTimeOf(process);
            _process.OutputDataReceived += (_, e) => Keep(e.Data, error: false);
            _process.ErrorDataReceived += (_, e) => Keep(e.Data, error: true);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        // read once, because a released Process no longer tells its id
        public int Id { get; }

        public DateTime? StartedAt { get; }

        // a released handle keeps what it knew, so a watch that still holds it can tell how the process ended
        public bool HasExited => _disposed ? _exitCodeAtRelease is not null : _process.HasExited;

        public bool HasEnded
        {
            get
            {
                if (!HasExited)
                    return false;

                lock (_outputGate)
                    return _outputClosed && _errorClosed;
            }
        }

        public int? ExitCode => _disposed ? _exitCodeAtRelease : _process.HasExited ? _process.ExitCode : null;

        public IReadOnlyList<string> RecentOutput
        {
            get
            {
                lock (_outputGate)
                    return _output.ToArray();
            }
        }

        /// <summary>A release of the handle ends the wait with false, and a wait on a released handle throws.</summary>
        public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _released.Token);
            window.CancelAfter(timeout);
            try
            {
                // also waits for the output to be read to its end, which a process that took over the streams delays
                await _process.WaitForExitAsync(window.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        private void Keep(string? line, bool error)
        {
            lock (_outputGate)
            {
                // the reader reports the end of a stream as a null line
                if (line is null)
                {
                    if (error)
                        _errorClosed = true;
                    else
                        _outputClosed = true;
                    return;
                }

                _output.Enqueue(line);
                while (_output.Count > OutputLines)
                    _output.Dequeue();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _exitCodeAtRelease = _process.HasExited ? _process.ExitCode : null;
            _disposed = true;

            // the waits end on the thread pool, not inside the lock of a launcher that releases the handle
            _ = _released.CancelAsync();
            try
            {
                _process.CancelOutputRead();
                _process.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
                // the reads had not started or have already finished
            }

            _process.Dispose();
        }
    }
}
