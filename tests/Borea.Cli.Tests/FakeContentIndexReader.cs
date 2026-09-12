using Borea.Core.Index;

namespace Borea.Cli.Tests;

internal sealed class FakeContentIndexReader : IContentIndexReader
{
    public Task<ContentIndexSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ContentIndexSnapshot(
            1,
            Array.Empty<ContentIndexListing>(),
            Array.Empty<ContentIndexPack>(),
            null,
            Array.Empty<ContentIndexDiagnostic>()));
}
