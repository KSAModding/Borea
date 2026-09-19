using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Storage.Listings;
using Borea.Storage.Tests.Mods;

namespace Borea.Storage.Tests.Listings;

public sealed class ListingArchiveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BoreaListingArchive_" + Guid.NewGuid().ToString("N"));

    public ListingArchiveTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Read_OneFolderWithModTomlAndItsDll_IsACodeModInThatFolder()
    {
        var facts = Read(("MyMod/mod.toml", "name = \"MyMod\"\n"), ("MyMod/MyMod.dll", "binary"), ("README.md", "text"));

        Assert.Equal("MyMod", facts.Root);
        Assert.True(facts.ModToml);
        Assert.Equal("MyMod", facts.EntryAssembly);
        Assert.True(facts.IsCodeMod);
    }

    [Fact]
    public void Read_EntryAssemblyOfTheStarMapSection_NamesTheDll()
    {
        var facts = Read(("MyMod/mod.toml", "name = \"MyMod\"\n[StarMap]\nEntryAssembly = \"MyMod.Core\"\n"), ("MyMod/mymod.core.DLL", "binary"));

        Assert.Equal("MyMod.Core", facts.EntryAssembly);
        Assert.True(facts.IsCodeMod);
    }

    [Fact]
    public void Read_ContentModWithoutDll_IsNoCodeMod()
    {
        var facts = Read(("KSP-Redux/mod.toml", "name = \"KSP-Redux\"\n"), ("KSP-Redux/Planets/Kerbin.xml", "<x/>"), ("KSP-Redux/Sub/KSP-Redux.dll", "not at the root"));

        Assert.Equal("KSP-Redux", facts.Root);
        Assert.False(facts.IsCodeMod);
    }

    [Fact]
    public void Read_SeveralFolders_TheOneWithModTomlIsTheRoot()
    {
        var facts = Read(("Docs/readme.txt", "text"), ("MyMod/mod.toml", "name = \"MyMod\"\n"));

        Assert.Equal("MyMod", facts.Root);
        Assert.Equal(["Docs", "MyMod"], facts.TopLevelFolders);
    }

    [Fact]
    public void Read_OneFolderWithoutModToml_IsStillTheRoot()
    {
        var facts = Read(("MyMod/data.xml", "<x/>"));

        Assert.Equal("MyMod", facts.Root);
        Assert.False(facts.ModToml);
    }

    [Theory]
    [InlineData("A/one.txt", "B/two.txt")]
    [InlineData("mod.toml", "MyMod.dll")]
    public void Read_NoSingleFolder_DerivesNoRoot(string first, string second)
    {
        var facts = Read((first, "x"), (second, "x"));

        Assert.Null(facts.Root);
        Assert.False(facts.IsCodeMod);
    }

    [Fact]
    public void Read_ModTomlThatIsNotToml_Throws()
    {
        var error = Assert.Throws<InvalidDataException>(() => Read(("MyMod/mod.toml", "name = ")));

        Assert.Contains("is not valid TOML", error.Message);
    }

    [Fact]
    public void Read_BytesThatAreNoZip_Throw()
    {
        var path = Path.Combine(_folder, "broken.zip");
        File.WriteAllText(path, "no zip");

        Assert.Throws<InvalidDataException>(() => ListingArchive.Read(path));
    }

    [Fact]
    public async Task ReadAsync_DownloadsTheLatestArchiveReadsItAndDeletesIt()
    {
        var downloader = new FakeModDownloader { Bytes = TestArchives.Build(("MyMod/mod.toml", "name = \"MyMod\"\n"), ("MyMod/MyMod.dll", "binary")) };
        var reader = new ListingSourceReader(new FakeHosts(Latest("https://example.com/MyMod.zip", 100)), downloader, _folder, IListingSourceReader.MaxArchiveBytes);

        var source = await reader.ReadAsync(new ListingSourceReference.GitHub("owner", "MyMod"));

        Assert.Null(source.ArchiveProblem);
        Assert.Equal("MyMod", source.Archive!.Root);
        Assert.True(source.Archive.IsCodeMod);
        Assert.StartsWith(_folder, downloader.ArchivePaths.Single(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task ReadAsync_ArchiveAboveTheLimit_IsNotDownloaded()
    {
        var downloader = new FakeModDownloader();
        var reader = new ListingSourceReader(new FakeHosts(Latest("https://example.com/MyMod.zip", 2000)), downloader, _folder, 1000);

        var source = await reader.ReadAsync(new ListingSourceReference.GitHub("owner", "MyMod"));

        Assert.Null(source.Archive);
        Assert.Contains("above the limit of 1000 bytes", source.ArchiveProblem);
        Assert.Empty(downloader.ArchivePaths);
    }

    [Fact]
    public async Task ReadAsync_DownloadThatGrowsAboveTheLimit_StopsAndDeletesTheFile()
    {
        var downloader = new FakeModDownloader { Bytes = new byte[2000] };
        var reader = new ListingSourceReader(new FakeHosts(Latest("https://example.com/MyMod.zip", null)), downloader, _folder, 1000);

        var source = await reader.ReadAsync(new ListingSourceReference.GitHub("owner", "MyMod"));

        Assert.Contains("above the limit", source.ArchiveProblem);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task ReadAsync_SeveralArchivesAndNoneNamedAfterTheListing_SaysSo()
    {
        var hosts = new FakeHosts(new ListingHostRelease("v1.0.0", "1.0.0", null, null, ["a.zip", "b.zip"]));
        var reader = new ListingSourceReader(hosts, new FakeModDownloader(), _folder, 1000);

        var source = await reader.ReadAsync(new ListingSourceReference.GitHub("owner", "MyMod"));

        Assert.Contains("carries 2 archives", source.ArchiveProblem);
    }

    [Fact]
    public async Task ReadAsync_FailedDownload_KeepsTheHostFacts()
    {
        var downloader = new FakeModDownloader { Failure = new DownloadFailedException("No source served the archive.") };
        var reader = new ListingSourceReader(new FakeHosts(Latest("https://example.com/MyMod.zip", 10)), downloader, _folder, 1000);

        var source = await reader.ReadAsync(new ListingSourceReference.GitHub("owner", "MyMod"));

        Assert.Equal("No source served the archive.", source.ArchiveProblem);
        Assert.Equal("MyMod", source.Host.Name);
    }

    [Theory]
    [InlineData("Advanced Flight Computer", "Advanced-Flight-Computer")]
    [InlineData("  ", "listing")]
    public async Task ReadAsync_SpaceDockMod_DownloadsUnderItsName(string name, string expected)
    {
        string? downloaded = null;
        var downloader = new FakeModDownloader
        {
            Bytes = TestArchives.Build(("MyMod/mod.toml", "name = \"MyMod\"\n")),
            Downloading = (release, _) =>
            {
                downloaded = release.ModId;
                return Task.CompletedTask;
            },
        };
        var reader = new ListingSourceReader(new FakeHosts(Latest("https://spacedock.info/mod/7/x/download/1.0.0", 100), name), downloader, _folder, 1000);

        await reader.ReadAsync(new ListingSourceReference.SpaceDock(7));

        Assert.Equal(expected, downloaded);
    }

    private ListingArchiveFacts Read(params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, TestArchives.Build(entries));
        return ListingArchive.Read(path);
    }

    private static ListingHostRelease Latest(string url, long? size) => new("v1.0.0", "1.0.0", url, size, []);

    private sealed class FakeHosts(ListingHostRelease? latest, string name = "MyMod") : IListingHostClient
    {
        public Task<ListingHostFacts> ReadAsync(ListingSourceReference source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListingHostFacts(source, name, null, null, [], [], new ListingReleases("owner/MyMod", null), latest));
    }
}
