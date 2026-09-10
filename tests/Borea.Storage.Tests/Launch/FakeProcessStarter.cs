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

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
