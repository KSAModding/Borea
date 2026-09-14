using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Logging;

public sealed class LoggingModInstaller : IModInstaller
{
    private readonly IBoreaLog _log;

    public IModInstaller Inner { get; }

    public LoggingModInstaller(IModInstaller inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<InstallResult> InstallAsync(
        Guid instanceId,
        ModVersionMetadata release,
        InstallReason reason,
        bool enable,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => LogAsync(instanceId, release, reason, progress, recorder => Inner.InstallAsync(instanceId, release, reason, enable, recorder, cancellationToken), result => result);

    public Task<GuardedInstallResult> InstallGuardedAsync(
        Guid instanceId,
        ModVersionMetadata release,
        InstallReason reason,
        bool enable,
        InstallPlanningState expectedState,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => LogAsync(instanceId, release, reason, progress, recorder => Inner.InstallGuardedAsync(instanceId, release, reason, enable, expectedState, recorder, cancellationToken), result => result.Result);

    private async Task<T> LogAsync<T>(
        Guid instanceId,
        ModVersionMetadata release,
        InstallReason reason,
        IProgress<InstallProgress>? progress,
        Func<InstallPhaseRecorder, Task<T>> install,
        Func<T, InstallResult> resultOf)
    {
        ArgumentNullException.ThrowIfNull(release);

        var name = $"{release.ModId} {release.Version}";
        _log.Write($"Install of {name} into instance {instanceId} started, reason {reason}.");
        var recorder = new InstallPhaseRecorder(progress);
        try
        {
            var result = await install(recorder).ConfigureAwait(false);
            var download = resultOf(result).Download;
            _log.Write($"Install of {name} finished, {download.BytesDownloaded} bytes from {download.Url}.");
            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Install of {name} cancelled {recorder.PhaseText}.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Install of {name} failed {recorder.PhaseText}.", exception);
            throw;
        }
    }
}
