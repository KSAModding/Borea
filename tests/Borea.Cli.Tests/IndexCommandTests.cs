using Borea.Core.Index;

namespace Borea.Cli.Tests;

public sealed class IndexCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Theory]
    [InlineData(ContentIndexFetchResult.Downloaded, "Content index: downloaded.")]
    [InlineData(ContentIndexFetchResult.NotModified, "Content index: not modified.")]
    public async Task Refresh_ReportsTheFetchResult(ContentIndexFetchResult result, string expectedOutput)
    {
        _host.IndexFetcher.Result = result;

        var run = await _host.RunAsync("index", "refresh");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(expectedOutput, run.Output);
        Assert.Equal(_host.Paths.GetIndexPath(), _host.IndexFetcher.DestinationPath);
    }

    public void Dispose() => _host.Dispose();
}
