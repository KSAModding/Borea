using Borea.Core.Index;

namespace Borea.Cli.Tests;

internal sealed class FakeContentIndexFetcher : IContentIndexFetcher
{
    public ContentIndexFetchResult Result { get; set; } = ContentIndexFetchResult.Downloaded;

    public string? DestinationPath { get; private set; }

    public Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DestinationPath = destinationPath;
        return Task.FromResult(Result);
    }
}
