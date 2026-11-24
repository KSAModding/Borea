using Borea.Core.Launch;

namespace Borea.Storage.Launch;

public interface IProcessStarter
{
    /// <summary>Throws the operating system's error when the process does not start.</summary>
    IStartedProcess Start(LaunchPlan plan);

    /// <summary>
    /// The process with this id that started at <paramref name="startedAt"/>, while it runs, so a Borea that did not
    /// start it can watch it. Null when it has ended or another process has the id now.
    /// </summary>
    IStartedProcess? Find(int processId, DateTime startedAt) => null;
}
