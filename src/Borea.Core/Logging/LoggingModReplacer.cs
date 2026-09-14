using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Logging;

public sealed class LoggingModReplacer : IModReplacer
{
    private readonly IBoreaLog _log;

    public IModReplacer Inner { get; }

    public LoggingModReplacer(IModReplacer inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<ModReplacementResult> ReplaceAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => LogAsync(instanceId, expectedCurrent, replacement, progress, recorder => Inner.ReplaceAsync(instanceId, expectedCurrent, replacement, recorder, cancellationToken), result => result);

    public Task<GuardedModReplacementResult> ReplaceGuardedAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        InstallPlanningState expectedState,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => LogAsync(instanceId, expectedCurrent, replacement, progress, recorder => Inner.ReplaceGuardedAsync(instanceId, expectedCurrent, replacement, expectedState, recorder, cancellationToken), result => result.Result);

    private async Task<T> LogAsync<T>(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        IProgress<InstallProgress>? progress,
        Func<InstallPhaseRecorder, Task<T>> replace,
        Func<T, ModReplacementResult> resultOf)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);

        var name = $"{replacement.ModId} {expectedCurrent.Version} with {replacement.Version}";
        _log.Write($"Replacement of {name} in instance {instanceId} started.");
        var recorder = new InstallPhaseRecorder(progress);
        try
        {
            var result = await replace(recorder).ConfigureAwait(false);
            var replaced = resultOf(result);
            var kept = replaced.RetainedRecoveryDirectory is null ? "" : $" The previous files stay in {replaced.RetainedRecoveryDirectory}.";
            _log.Write($"Replacement of {name} finished, {replaced.Download.BytesDownloaded} bytes from {replaced.Download.Url}.{kept}");
            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Replacement of {name} cancelled {recorder.PhaseText}.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Replacement of {name} failed {recorder.PhaseText}.", exception);
            throw;
        }
    }
}
