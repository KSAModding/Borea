using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.Cli.Tests;

internal sealed class FakeProcessStarter : IProcessStarter
{
    public List<LaunchPlan> Plans { get; } = new();

    /// <summary>Thrown by every start after the plan is recorded, when set.</summary>
    public Exception? Failure { get; set; }

    public IStartedProcess Start(LaunchPlan plan)
    {
        Plans.Add(plan);

        if (Failure is not null)
            throw Failure;

        return new FakeStartedProcess();
    }

    private sealed class FakeStartedProcess : IStartedProcess
    {
        public int Id => 42;

        public bool HasExited => false;

        public void Dispose()
        {
        }
    }
}
