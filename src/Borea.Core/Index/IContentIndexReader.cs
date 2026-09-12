namespace Borea.Core.Index;

/// <summary>
/// Reads the cached snapshot asynchronously. A successful read guarantees a
/// supported root envelope and returns supported entries with diagnostics for
/// isolated entry failures.
/// </summary>
public interface IContentIndexReader
{
    Task<ContentIndexSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
