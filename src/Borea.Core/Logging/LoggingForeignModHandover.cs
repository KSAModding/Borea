using Borea.Core.Mods;

namespace Borea.Core.Logging;

public sealed class LoggingForeignModHandover : IForeignModHandover
{
    private readonly IBoreaLog _log;

    public IForeignModHandover Inner { get; }

    public LoggingForeignModHandover(IForeignModHandover inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<ModHandoverResult> TakeOwnershipAsync(
        Guid instanceId,
        string modId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var name = $"'{modId}' in instance {instanceId}";
        _log.Write($"Handover of {name} started.");
        var recorder = new InstallPhaseRecorder(progress);
        try
        {
            var result = await Inner.TakeOwnershipAsync(instanceId, modId, recorder, cancellationToken).ConfigureAwait(false);
            var kept = result.RetainedRecoveryDirectory is null ? "" : $" The previous files stay in {result.RetainedRecoveryDirectory}.";
            _log.Write($"Handover of {name} finished with {result.Installed.Version}, {result.Download.BytesDownloaded} bytes from {result.Download.Url}.{kept}");
            return result;
        }
        catch (ModReplacementRecoveryException exception)
        {
            _log.Write($"Handover of {name} failed {recorder.PhaseText}, and the previous files stay in {exception.RecoveryDirectory}.", exception);
            throw;
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Handover of {name} cancelled {recorder.PhaseText}.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Handover of {name} failed {recorder.PhaseText}.", exception);
            throw;
        }
    }
}
