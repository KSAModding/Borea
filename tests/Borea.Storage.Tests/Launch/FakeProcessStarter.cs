using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.Storage.Tests.Launch;

/// <summary>
/// Records every plan and hands out processes whose exit a test controls.
/// </summary>
internal sealed class FakeProcessStarter : IProcessStarter
{
    private int _nextId = 1000;

    public List<LaunchPlan> Plans { get; } = new();

    public List<FakeStartedProcess> Processes { get; } = new();

    /// <summary>Thrown by the next start when set.</summary>
    public Exception? Failure { get; set; }

    public IStartedProcess Start(LaunchPlan plan)
    {
        Plans.Add(plan);

        if (Failure is not null)
            throw Failure;

        var process = new FakeStartedProcess(_nextId++);
        Processes.Add(process);
        return process;
    }
}

internal sealed class FakeStartedProcess : IStartedProcess
{
    public FakeStartedProcess(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public bool HasExited { get; set; }

    public int? ExitCode { get; set; }

    public List<string> Output { get; } = new();

    public IReadOnlyList<string> RecentOutput => Output;

    /// <summary>Answers at once, so a watch over a fake never waits for real time.</summary>
    public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(HasExited);

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
