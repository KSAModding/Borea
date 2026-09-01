namespace Borea.Network.Index
{
    public interface IContentIndexFetcher
    {
        Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default);
    }
}
