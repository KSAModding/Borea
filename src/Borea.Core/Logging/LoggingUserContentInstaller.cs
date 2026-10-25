using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.UserContent;

namespace Borea.Core.Logging;

/// <summary>Writes each install, update and removal of a vehicle or save folder to the log. The checks write nothing.</summary>
public sealed class LoggingUserContentInstaller : IUserContentInstaller
{
    private readonly IBoreaLog _log;

    public IUserContentInstaller Inner { get; }

    public LoggingUserContentInstaller(IUserContentInstaller inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<UserContentResult> CheckInstallAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken = default)
        => Inner.CheckInstallAsync(instanceId, release, name, cancellationToken);

    public Task<UserContentResult> CheckUpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken = default)
        => Inner.CheckUpdateAsync(instanceId, installed, release, name, discardChanges, cancellationToken);

    public Task<UserContentResult> InstallAsync(Guid instanceId, ModVersionMetadata release, string? name, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        => LogAsync($"Install of {release?.ModId} {release?.Version} into instance {instanceId}", () => Inner.InstallAsync(instanceId, release!, name, beforeMove, progress, cancellationToken));

    public Task<UserContentResult> UpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        => LogAsync($"Update of {installed?.ListingId} {installed?.Version} to {release?.Version} in instance {instanceId}", () => Inner.UpdateAsync(instanceId, installed!, release!, name, discardChanges, beforeMove, progress, cancellationToken));

    public Task<UserContentResult> RemoveAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken = default)
        => LogAsync($"Removal of {installed?.ListingId} {installed?.Version} from instance {instanceId}", () => Inner.RemoveAsync(instanceId, installed!, discardChanges, cancellationToken));

    private async Task<UserContentResult> LogAsync(string what, Func<Task<UserContentResult>> action)
    {
        _log.Write($"{what} started.");
        try
        {
            var result = await action().ConfigureAwait(false);
            _log.Write($"{what} ended: {result.Outcome}, folder '{result.FolderName}'.");
            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Write($"{what} cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"{what} failed.", exception);
            throw;
        }
    }
}
