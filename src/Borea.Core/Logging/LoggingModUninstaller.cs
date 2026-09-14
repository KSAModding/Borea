using Borea.Core.Mods;

namespace Borea.Core.Logging;

public sealed class LoggingModUninstaller : IModUninstaller
{
    private readonly IBoreaLog _log;

    public IModUninstaller Inner { get; }

    public LoggingModUninstaller(IModUninstaller inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
    {
        _log.Write($"Removal of {modId} from instance {instanceId} started.");
        try
        {
            await Inner.UninstallAsync(instanceId, modId, cancellationToken).ConfigureAwait(false);
            _log.Write($"Removal of {modId} finished.");
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Removal of {modId} cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Removal of {modId} failed.", exception);
            throw;
        }
    }
}
