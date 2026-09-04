using System.ComponentModel;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Launch;

/// <summary>
/// ILauncher over the configured loader directories and a process starter.
/// It remembers every launch per instance until the process exits, so a
/// second launch of a running instance is refused.
/// </summary>
public sealed class LoaderLauncher : ILauncher, IDisposable
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IProcessStarter _starter;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, IStartedProcess> _running = new();

    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
    }

    public LaunchResult Launch(Instance instance, ModMetadata? loader)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        if (loader is null)
        {
            return LaunchResult.Failed(
                LaunchOutcome.NoLoader,
                "No mod loader is set for this launch. The game reads no instance path on its own, so install a loader first.");
        }

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader can start the game.", nameof(loader));

        lock (_gate)
        {
            Forget(exited: true);

            if (_running.ContainsKey(instance.InstanceId))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.AlreadyRunning,
                    $"Instance '{instance.Name}' is already running from a launch Borea started. Close the game first.");
            }

            var launch = loader.Provides?.Launch;
            if (launch is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoLaunchTarget,
                    $"The listing of {loader.Name} does not say what to run, so Borea cannot start it.");
            }

            var handover = InstanceHandover.Known(loader.ModId);
            if (handover is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoInstanceHandover,
                    $"Borea does not know how {loader.Name} takes an instance, so it cannot start one with it.");
            }

            var loaderDirectory = _pathProvider.GetLoaderDirectoryPath(loader.ModId);
            if (loaderDirectory is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoLoaderDirectory,
                    $"Borea does not know where {loader.Name} is installed. Set its directory in the settings.");
            }

            var plan = LaunchPlan.ForLoader(
                Path.GetFullPath(loaderDirectory),
                launch,
                handover,
                Path.GetFullPath(_pathProvider.GetInstanceRoot(instance.InstanceId)));

            if (!File.Exists(plan.Executable))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.LaunchTargetMissing,
                    $"'{plan.Executable}' is not there. Reinstall {loader.Name} or correct its directory in the settings.",
                    plan);
            }

            IStartedProcess process;
            try
            {
                process = _starter.Start(plan);
            }
            catch (Win32Exception exception)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.StartFailed,
                    $"The system did not start '{plan.Executable}': {exception.Message}",
                    plan);
            }

            _running[instance.InstanceId] = process;

            return LaunchResult.Success(
                plan,
                process.Id,
                $"Started {loader.Name} for instance '{instance.Name}'.");
        }
    }

    public bool IsRunning(Guid instanceId)
    {
        lock (_gate)
        {
            Forget(exited: true);
            return _running.ContainsKey(instanceId);
        }
    }

    /// <summary>Releases the handles. The processes keep running.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            Forget(exited: false);
        }
    }

    /// <summary>Drops the exited launches, or every launch, and releases their handles.</summary>
    private void Forget(bool exited)
    {
        foreach (var (instanceId, process) in _running.ToArray())
        {
            if (exited && !process.HasExited)
                continue;

            _running.Remove(instanceId);
            process.Dispose();
        }
    }
}
