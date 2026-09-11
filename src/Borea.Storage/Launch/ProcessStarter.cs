using System.Diagnostics;
using Borea.Core.Launch;

namespace Borea.Storage.Launch;

/// <summary>
/// Starts a plan as an operating system process. The process inherits Borea's
/// environment plus the plan's variables and keeps Borea's console.
/// </summary>
public sealed class ProcessStarter : IProcessStarter
{
    public IStartedProcess Start(LaunchPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        // UseShellExecute off, so the environment and the argument list reach the process.
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.Executable,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
        };

        foreach (var argument in plan.Arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var (name, value) in plan.EnvironmentVariables)
            startInfo.Environment[name] = value;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"No process was started for '{plan.Executable}'.");

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
