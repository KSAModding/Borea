using System.Diagnostics;
using Borea.Core.Launch;

namespace Borea.Storage.Launch;

/// <summary>
/// Starts a plan as an operating system process with Borea's environment plus the
/// plan's variables. The process gets none of Borea's standard handles, so a caller
/// that pipes Borea's output does not wait for the game.
/// </summary>
public sealed class ProcessStarter : IProcessStarter
{
    // Handle inheritance is process-wide, so starts must not overlap.
    private static readonly object StartGate = new();

    public IStartedProcess Start(LaunchPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        // UseShellExecute off, so the environment and the argument list reach the process.
        // The streams are pipes whose Borea ends are closed at once, and a console loader opens no window.
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
        process.StandardOutput.Close();
        process.StandardError.Close();

        return new StartedProcess(process);
    }

    private sealed class StartedProcess : IStartedProcess
    {
        private readonly Process _process;

        public StartedProcess(Process process)
        {
            _process = process;
        }

        public int Id => _process.Id;

        public bool HasExited => _process.HasExited;

        public void Dispose() => _process.Dispose();
    }
}
