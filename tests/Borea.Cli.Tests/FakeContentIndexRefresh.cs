using Borea.Core.Index;

namespace Borea.Cli.Tests;

internal sealed class FakeContentIndexRefresh : IContentIndexRefresh
{
    public ContentIndexRefreshStatus Status { get; set; } = new(ContentIndexRefreshOutcome.NotAttempted, CachedAt: null);

    public TimeSpan RevalidationInterval => TimeSpan.FromMinutes(10);

    public Task<ContentIndexSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
