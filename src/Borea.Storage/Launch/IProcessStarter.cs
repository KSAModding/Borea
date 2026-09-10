using Borea.Core.Launch;

namespace Borea.Storage.Launch;

public interface IProcessStarter
{
    /// <summary>Throws the operating system's error when the process does not start.</summary>
    IStartedProcess Start(LaunchPlan plan);
}
