using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.Cli.Tests;

internal sealed class FakeProcessStarter : IProcessStarter
{
    public List<LaunchPlan> Plans { get; } = new();

    /// <summary>Thrown by every start after the plan is recorded, when set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, a started process has already exited with this code and <see cref="CrashOutput"/>.</summary>
    public int? CrashExitCode { get; set; }

    public List<string> CrashOutput { get; } = new();

    public IStartedProcess Start(LaunchPlan plan)
    {
        Plans.Add(plan);

        if (Failure is not null)
            throw Failure;

        return new FakeStartedProcess(CrashExitCode, CrashOutput.ToArray());
    }

    private sealed class FakeStartedProcess(int? exitCode, IReadOnlyList<string> output) : IStartedProcess
    {
        public int Id => 42;

        public bool HasExited => exitCode is not null;

        public int? ExitCode => exitCode;

        public IReadOnlyList<string> RecentOutput => output;

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(HasExited);

        public void Dispose()
        {
        }
    }
}
