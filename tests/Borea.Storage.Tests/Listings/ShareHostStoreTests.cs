using Borea.Core.Sharing;
using Borea.Storage.Listings;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Listings;

public sealed class ShareHostStoreTests : IDisposable
{
    /// <summary>The example of RFC 0094.</summary>
    private const string Example = """
        [ksamods]
        kind = "share"
        name = "ksamods.gg"
        storage = "ksamods-content/share"
        bot = 98765432
        api = "https://ksamods.gg/api/share/v1/"
        pages = "https://ksamods-content.github.io/share/"
        types = ["vehicle", "save"]
        max_bytes = { vehicle = 10485760, save = 104857600 }
        new_listings_per_day = 5
        policy = "https://ksamods.gg/policy"
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaShareHosts_" + Guid.NewGuid().ToString("N"));
    private readonly TestGamePathProvider _paths;

    public ShareHostStoreTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new TestGamePathProvider(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task GetAsync_ExampleOfTheRfc_IsReadAndCached()
    {
        var host = Assert.Single(await new ShareHostStore(new Fetcher(() => Example), _paths).GetAsync());

        Assert.Equal(("ksamods", "ksamods.gg", "https://ksamods.gg/api/share/v1/"), (host.Key, host.Name, host.Api.AbsoluteUri));
        Assert.Equal(["vehicle", "save"], host.Types);
        Assert.Equal(10485760, host.MaxBytesFor("vehicle"));
        Assert.Null(host.MaxBytesFor("mod"));
        Assert.Equal("https://ksamods.gg/", host.Page.AbsoluteUri);
        Assert.Equal("https://ksamods.gg/policy", host.Policy?.AbsoluteUri);
        Assert.Equal(Example, await File.ReadAllTextAsync(_paths.GetShareHostsPath()));
    }

    [Fact]
    public async Task GetAsync_DownloadFails_UsesTheCache()
    {
        await File.WriteAllTextAsync(_paths.GetShareHostsPath(), Example);

        var hosts = await new ShareHostStore(new Fetcher(() => throw new HttpRequestException("offline")), _paths).GetAsync();

        Assert.Equal("ksamods", Assert.Single(hosts).Key);
    }

    [Fact]
    public async Task GetAsync_BodyReadFails_UsesTheCache()
    {
        await File.WriteAllTextAsync(_paths.GetShareHostsPath(), Example);

        var hosts = await new ShareHostStore(new Fetcher(() => throw new HttpIOException(HttpRequestError.ResponseEnded)), _paths).GetAsync();

        Assert.Equal("ksamods", Assert.Single(hosts).Key);
    }

    [Fact]
    public async Task GetAsync_SecondCall_DownloadsOnlyOnce()
    {
        var downloads = 0;
        var store = new ShareHostStore(new Fetcher(() => { downloads++; return Example; }), _paths);

        await store.GetAsync();
        var hosts = await store.GetAsync();

        Assert.Equal(1, downloads);
        Assert.Equal("ksamods", Assert.Single(hosts).Key);
    }

    [Fact]
    public async Task GetAsync_AfterTheCache_DownloadsAgain()
    {
        await File.WriteAllTextAsync(_paths.GetShareHostsPath(), Example);
        var offline = true;
        var store = new ShareHostStore(new Fetcher(() => offline ? throw new HttpRequestException("offline") : null), _paths);

        await store.GetAsync();
        offline = false;

        Assert.Empty(await store.GetAsync());
    }

    [Fact]
    public async Task GetAsync_NoFileInContentIndex_RegistersNoHostAndDropsTheCache()
    {
        await File.WriteAllTextAsync(_paths.GetShareHostsPath(), Example);

        Assert.Empty(await new ShareHostStore(new Fetcher(() => null), _paths).GetAsync());
        Assert.False(File.Exists(_paths.GetShareHostsPath()));
    }

    [Fact]
    public async Task GetAsync_TextThatIsNoToml_IsNeitherUsedNorCached()
    {
        Assert.Empty(await new ShareHostStore(new Fetcher(() => "[ksamods"), _paths).GetAsync());
        Assert.False(File.Exists(_paths.GetShareHostsPath()));
    }

    [Theory]
    [InlineData("[ksamods]", "[github]")]
    [InlineData("[ksamods]", "[KSAmods]")]
    [InlineData("kind = \"share\"", "kind = \"mirror\"")]
    [InlineData("https://ksamods.gg/api/share/v1/", "http://ksamods.gg/api/share/v1/")]
    [InlineData("types = [\"vehicle\", \"save\"]", "types = \"vehicle\"")]
    [InlineData("vehicle = 10485760", "vehicle = 0")]
    public async Task GetAsync_TableBoreaCannotUse_IsSkippedAndTheOthersStay(string from, string to)
    {
        var other = Example.Replace("[ksamods]", "[other]", StringComparison.Ordinal);
        var text = Example.Replace(from, to, StringComparison.Ordinal) + "\n\n" + other;

        Assert.Equal("other", Assert.Single(await new ShareHostStore(new Fetcher(() => text), _paths).GetAsync()).Key);
    }

    private sealed class Fetcher(Func<string?> fetch) : IShareHostFetcher
    {
        public Task<string?> FetchHostsAsync(CancellationToken cancellationToken = default) => Task.FromResult(fetch());
    }
}
