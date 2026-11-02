using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.UserContent;

namespace Borea.Core.Sharing;

/// <summary>
/// Reports each finished install or update of a vehicle or save to the share host whose downloads serve its archive, so that the
/// host can count it. Every install counts, also one whose archive came from the preview cache, and the host counts each player
/// once a day by itself. The report runs in the background after the result, so no answer and no failure of it delays or changes
/// the install. A preview or a still fetches its archive without this installer and so sends no report.
/// </summary>
public sealed class InstallReportingUserContentInstaller : IUserContentInstaller
{
    private readonly IUserContentInstaller _inner;
    private readonly IShareHostSource _hosts;
    private readonly IShareHostClient _client;
    private readonly IBoreaLog _log;
    private readonly object _gate = new();
    private Task _reports = Task.CompletedTask;

    /// <param name="hosts">The hosts of the cached snapshot, so that a report never fetches the index.</param>
    public InstallReportingUserContentInstaller(IUserContentInstaller inner, IShareHostSource hosts, IShareHostClient client, IBoreaLog log)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Completes when every report that started so far has ended.</summary>
    public Task Reports
    {
        get
        {
            lock (_gate)
                return _reports;
        }
    }

    public Task<UserContentResult> CheckInstallAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken = default)
        => _inner.CheckInstallAsync(instanceId, release, name, cancellationToken);

    public Task<UserContentResult> CheckUpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken = default)
        => _inner.CheckUpdateAsync(instanceId, installed, release, name, discardChanges, cancellationToken);

    public async Task<UserContentResult> InstallAsync(Guid instanceId, ModVersionMetadata release, string? name, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = await _inner.InstallAsync(instanceId, release, name, beforeMove, progress, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == UserContentOutcome.Installed)
            StartReport(release);

        return result;
    }

    public async Task<UserContentResult> UpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = await _inner.UpdateAsync(instanceId, installed, release, name, discardChanges, beforeMove, progress, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == UserContentOutcome.Updated)
            StartReport(release);

        return result;
    }

    public Task<UserContentResult> RemoveAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken = default)
        => _inner.RemoveAsync(instanceId, installed, discardChanges, cancellationToken);

    /// <summary>
    /// Waits up to <paramref name="limit"/> for the reports that are still on their way. The command line ends its process right
    /// after it closes the services, which would drop such a report.
    /// </summary>
    public void WaitForReports(TimeSpan limit) => Reports.Wait(limit);

    private void StartReport(ModVersionMetadata release)
    {
        var report = Task.Run(() => ReportAsync(release));
        lock (_gate)
            _reports = Task.WhenAll(_reports, report);
    }

    private async Task ReportAsync(ModVersionMetadata release)
    {
        var what = $"the install of {release.ModId} {release.Version}";
        try
        {
            var type = release.Type == ContentType.Vehicle ? ListingFolders.VehicleType : ListingFolders.SaveType;
            if (!Uri.TryCreate(release.Download.Url, UriKind.Absolute, out var url))
                return;

            var hosts = await _hosts.GetAsync().ConfigureAwait(false);
            if (hosts.FirstOrDefault(host => host.Accepts(type) && host.ServesDownload(url)) is not { } shareHost)
                return;

            try
            {
                await _client.ReportInstallAsync(shareHost, type, release.ModId).ConfigureAwait(false);
                _log.Write($"Reported {what} to {shareHost.Name}.");
            }
            catch (ShareHostException exception)
            {
                _log.Write($"Could not report {what} to {shareHost.Name}: {exception.Message}");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Nothing awaits the report, so without this line its failure would go unseen.
            _log.Write($"Could not report {what}.", exception);
        }
    }
}
