using Borea.Core.Index;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

public sealed class ShareHostStoreTests
{
    [Fact]
    public async Task GetAsync_SnapshotFails_GivesNoHosts()
    {
        Assert.Empty(await new ShareHostStore(new FailingSnapshots()).GetAsync());
    }

    private sealed class FailingSnapshots : IContentIndexSnapshotProvider
    {
        public Task<ContentIndexSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ContentIndexSnapshot>(new HttpRequestException("offline"));
    }
}
