using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.Cli.Tests;

internal sealed class FakeProcessStarter : IProcessStarter
{
    public List<LaunchPlan> Plans { get; } = new();

    public IStartedProcess Start(LaunchPlan plan)
    {
        Plans.Add(plan);
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
