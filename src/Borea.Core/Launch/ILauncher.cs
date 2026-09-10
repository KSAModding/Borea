using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Starts the game for one instance through its mod loader.
/// </summary>
public interface ILauncher
{
    LaunchResult Launch(Instance instance, ModMetadata? loader);

    /// <summary>
    /// Whether a launch this launcher started for the instance is still
    /// running. A launch from an earlier session, a game started by hand, and
    /// the new process a loader starts when it restarts itself are not seen.
    /// </summary>
    bool IsRunning(Guid instanceId);
}
