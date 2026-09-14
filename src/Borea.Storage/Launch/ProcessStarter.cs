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

    private sealed class StartedProcess : IStartedProcess
    {
        private readonly Process _process;
        private readonly Queue<string> _output = new();
        private readonly object _outputGate = new();
        private bool _disposed;

        public StartedProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, e) => Keep(e.Data);
            _process.ErrorDataReceived += (_, e) => Keep(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public int Id => _process.Id;

        public bool HasExited => _process.HasExited;

        public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

        public IReadOnlyList<string> RecentOutput
        {
            get
            {
                lock (_outputGate)
                    return _output.ToArray();
            }
        }

        public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(timeout);
            try
            {
                // also waits for the output of an exited process to be read to its end
                await _process.WaitForExitAsync(window.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        private void Keep(string? line)
        {
            if (line is null)
                return;

            lock (_outputGate)
            {
                _output.Enqueue(line);
                while (_output.Count > OutputLines)
                    _output.Dequeue();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
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
