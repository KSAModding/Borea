using Borea.Core.Sharing;

namespace Borea.Core.Tests.Sharing;

public sealed class ShareHostTests
{
    private static readonly ShareHost Host = new("files", "files.example", new Uri("https://files.example/api/"), ["vehicle"], new Dictionary<string, long>())
    {
        Data = new Uri("https://data.example/"),
        Downloads = [new Uri("https://files.example/share/files/"), new Uri("https://cdn.example/")],
    };

    [Theory]
    [InlineData("https://files.example/share/files/vehicle/Rover/1.0.0", true)]
    [InlineData("https://cdn.example/archives/rover.zip", true)]
    [InlineData("http://files.example/share/files/vehicle/Rover/1.0.0", false)]
    [InlineData("https://user@files.example/share/files/vehicle/Rover/1.0.0", false)]
    [InlineData("https://files.example/share/filesother/rover.zip", false)]
    [InlineData("https://files.example/share/files/../other/rover.zip", false)]
    [InlineData("https://files.example/share/files/%2e%2e/other/rover.zip", false)]
    [InlineData("https://files.example/share/files/..%2Fother/rover.zip", false)]
    [InlineData("https://files.example.evil/share/files/rover.zip", false)]
    [InlineData("https://other.example/rover.zip", false)]
    public void ServesDownload_TakesOnlyHttpsUrlsUnderAPrefix(string url, bool served) =>
        Assert.Equal(served, Host.ServesDownload(new Uri(url)));

    [Fact]
    public void KeepsNoDownloadCount_NeedsDataAndADownloadPrefix()
    {
        Assert.True(Host.KeepsNoDownloadCount);
        Assert.False((Host with { Data = null }).KeepsNoDownloadCount);
        Assert.False((Host with { Downloads = [] }).KeepsNoDownloadCount);
    }
}
