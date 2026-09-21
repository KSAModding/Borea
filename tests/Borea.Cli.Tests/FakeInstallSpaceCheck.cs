using Borea.Core.Planning;

namespace Borea.Cli.Tests;

/// <summary>
/// Answers in place of the check against the real volumes, so a command test
/// does not depend on the free space of the machine it runs on. It accepts
/// every plan.
/// </summary>
internal sealed class FakeInstallSpaceCheck : IInstallSpaceCheck
{
    public void EnsureFits(InstallPlan plan)
    {
    }
}
