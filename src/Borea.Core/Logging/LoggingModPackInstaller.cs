using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Logging;

/// <summary>
/// Writes one line when a pack install does not complete. The installs of the members and the plan
/// have lines of their own, so a complete install and a plan without a write add nothing.
/// </summary>
public sealed class LoggingModPackInstaller : IModPackInstaller
{
    private readonly IBoreaLog _log;

    public IModPackInstaller Inner { get; }

    public LoggingModPackInstaller(IModPackInstaller inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<ModPackInstallResult> PlanAsync(ModPackInstallRequest request, CancellationToken cancellationToken = default) =>
        Inner.PlanAsync(request, cancellationToken);

    public Task<ModPackInstallResult> PlanNewAsync(string instanceName, ModPackInstallRequest request, CancellationToken cancellationToken = default) =>
        Inner.PlanNewAsync(instanceName, request, cancellationToken);

    public Task<ModPackInstallResult> InstallAsync(ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default) =>
        LogAsync(request, newInstanceName: null, () => Inner.InstallAsync(request, progress, stop, cancellationToken));

    public Task<ModPackInstallResult> CreateAndInstallAsync(string instanceName, ModPackInstallRequest request, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default) =>
        LogAsync(request, instanceName, () => Inner.CreateAndInstallAsync(instanceName, request, progress, stop, cancellationToken));

    private async Task<ModPackInstallResult> LogAsync(ModPackInstallRequest request, string? newInstanceName, Func<Task<ModPackInstallResult>> install)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pack = $"{request.Pack?.Id} {request.Pack?.Metadata?.Version}";
        var target = newInstanceName is null ? $"instance {request.InstanceId}" : $"the new instance '{newInstanceName}'";
        ModPackInstallResult result;
        try
        {
            result = await install().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Pack {pack} into {target} cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Pack {pack} into {target} failed.", exception);
            throw;
        }

        if (result.IsComplete)
            return result;

        var done = result.Members.Count(member => member.Status is ModPackMemberStatus.Installed or ModPackMemberStatus.Replaced or ModPackMemberStatus.AlreadyInstalled);
        var outcome = result.IsStopped ? "stopped on request" : "did not complete";
        var blockers = result.DescribeBlockers();
        _log.Write($"Pack {pack} into {target} {outcome}, {done} of {result.Members.Count} members done.{(blockers.Length == 0 ? string.Empty : $" Blocked by: {blockers}")}");
        return result;
    }
}
