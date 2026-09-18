using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Starts the game for one instance through its mod loader.
/// </summary>
public interface ILauncher
{
    /// <summary>
    /// Starts the loader with the instance handover, then the saved
    /// <see cref="Instance.LaunchArguments"/>, then <paramref name="arguments"/>
    /// for this launch only.
    /// </summary>
    LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null);

    /// <summary>
    /// Watches a launch that <see cref="Launch"/> just started until the game
    /// comes up, the loader stops, or the startup window ends. A loader that
    /// stops with a non-zero exit code in that time turns the result into
    /// <see cref="LaunchOutcome.ExitedEarly"/>, with its output and the mod its
    /// error names. Any other result is returned as it is.
    /// </summary>
    Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a launch this launcher started for the instance is still
    /// running. A launch from an earlier session, a game started by hand, and
    /// the new process a loader starts when it restarts itself are not seen.
    /// </summary>
    bool IsRunning(Guid instanceId);
}
