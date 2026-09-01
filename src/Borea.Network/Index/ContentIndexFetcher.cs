using System.Net.Http.Headers;

namespace Borea.Network.Index
{
    public sealed class ContentIndexFetcher : IContentIndexFetcher
    {
        private readonly HttpClient _client;
        private readonly Uri _indexUri;

        public ContentIndexFetcher(HttpClient client, Uri indexUri)
        {
            _client = client;
            _indexUri = indexUri;
        }

        public async Task<ContentIndexFetchResult> FetchAsync(string destinationPath, CancellationToken ct = default)
        {
            string etagPath = destinationPath + ".etag";
            string tempPath = destinationPath + ".tmp";

            using var request = new HttpRequestMessage(HttpMethod.Get, _indexUri);

            if (File.Exists(destinationPath) && File.Exists(etagPath))
            {
                string existingEtag = (await File.ReadAllTextAsync(etagPath, ct)).Trim();
                if (!string.IsNullOrEmpty(existingEtag))
                {
                    request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(existingEtag));
                }
            }

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                return ContentIndexFetchResult.NotModified;
            }

            response.EnsureSuccessStatusCode();

            await using (var tempStream = File.Create(tempPath))
            await using (var responseStream = await response.Content.ReadAsStreamAsync(ct))
            {
                await responseStream.CopyToAsync(tempStream, ct);
            }

            File.Move(tempPath, destinationPath, overwrite: true);

            string? newEtag = response.Headers.ETag?.Tag;

            if (newEtag is not null)
            {
                await File.WriteAllTextAsync(etagPath, newEtag, ct);
            }
            else if (File.Exists(etagPath))
            {
                File.Delete(etagPath);
            }

            return ContentIndexFetchResult.Downloaded;
        }
    }
}
