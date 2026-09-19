using Borea.Core.Listings;
using Borea.Storage.Listings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Listings;

public sealed class ListingSchemaStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaListingSchema_" + Guid.NewGuid().ToString("N"));
    private readonly TestGamePathProvider _paths;

    public ListingSchemaStoreTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new TestGamePathProvider(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task GetAsync_DownloadedSchema_IsUsedAndCached()
    {
        var downloaded = ListingSchemaStore.EmbeddedText.Replace("KSA content index: authored document", "newer", StringComparison.Ordinal);

        var schema = await new ListingSchemaStore(new Fetcher(() => downloaded), _paths).GetAsync();

        Assert.Equal(ListingSchemaOrigin.Downloaded, schema.Origin);
        Assert.Equal(downloaded, schema.Text);
        Assert.Equal(downloaded, await File.ReadAllTextAsync(_paths.GetListingSchemaPath()));
    }

    [Fact]
    public async Task GetAsync_FetchFailsWithACachedSchema_UsesTheCache()
    {
        var cached = ListingSchemaStore.EmbeddedText.Replace("KSA content index: authored document", "cached", StringComparison.Ordinal);
        await File.WriteAllTextAsync(_paths.GetListingSchemaPath(), cached);

        var schema = await new ListingSchemaStore(new Fetcher(() => throw new HttpRequestException("offline")), _paths).GetAsync();

        Assert.Equal(ListingSchemaOrigin.Cached, schema.Origin);
        Assert.Equal(cached, schema.Text);
    }

    [Fact]
    public async Task GetAsync_FetchFailsWithoutACache_FallsBackToTheEmbeddedSchema()
    {
        var schema = await new ListingSchemaStore(new Fetcher(() => throw new HttpRequestException("offline")), _paths).GetAsync();

        Assert.Equal(ListingSchemaOrigin.Embedded, schema.Origin);
        Assert.Equal(ListingSchemaStore.EmbeddedText, schema.Text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"$schema\": \"https://json-schema.org/draft/2020-12/schema\", \"pattern\": \"(\"}")]
    [InlineData("{\"$schema\": \"http://json-schema.org/draft-07/schema#\"}")]
    public async Task GetAsync_DownloadThatIsNoSchema_IsNeitherUsedNorCached(string text)
    {
        var schema = await new ListingSchemaStore(new Fetcher(() => text), _paths).GetAsync();

        Assert.Equal(ListingSchemaOrigin.Embedded, schema.Origin);
        Assert.False(File.Exists(_paths.GetListingSchemaPath()));
    }

    private sealed class Fetcher(Func<string> fetch) : IListingSchemaFetcher
    {
        public Task<string> FetchAsync(CancellationToken cancellationToken = default) => Task.FromResult(fetch());
    }
}
