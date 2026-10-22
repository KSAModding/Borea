using Borea.Core.Index;
using Borea.Core.Sharing;

namespace Borea.Storage.Listings;

/// <summary>
/// The share hosts of the current content-index snapshot, which holds hosts.toml as its hosts field (RFC 0104).
/// When no snapshot can be read, Borea offers no host, so it never offers a host that the index does not register.
/// </summary>
public sealed class ShareHostStore : IShareHostSource
{
    private readonly IContentIndexSnapshotProvider _snapshots;

    public ShareHostStore(IContentIndexSnapshotProvider snapshots)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
    }

    public async Task<IReadOnlyList<ShareHost>> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return snapshot.ShareHosts;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }
}
