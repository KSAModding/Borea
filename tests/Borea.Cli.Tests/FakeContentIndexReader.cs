using Borea.Core.Index;

namespace Borea.Cli.Tests;

internal sealed class FakeContentIndexReader : IContentIndexReader
{
    public ContentIndexSnapshot Snapshot { get; set; } = EmptySnapshot();

    public Func<CancellationToken, Task<ContentIndexSnapshot>>? Read { get; set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationToken CancellationToken { get; private set; }

    public Task<ContentIndexSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
        Started.TrySetResult();
        return Read?.Invoke(cancellationToken) ?? Task.FromResult(Snapshot);
    }

    private static ContentIndexSnapshot EmptySnapshot() => new(
        1,
        Array.Empty<ContentIndexListing>(),
        Array.Empty<ContentIndexPack>(),
        null,
        Array.Empty<ContentIndexDiagnostic>());
}
