using Borea.Core.Index;

namespace Borea.Cli.Tests;

internal sealed class FakeContentIndexRefresh : IContentIndexRefresh
{
    public ContentIndexRefreshStatus Status { get; set; } = new(ContentIndexRefreshOutcome.NotAttempted, CachedAt: null);

    public Task<ContentIndexSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
