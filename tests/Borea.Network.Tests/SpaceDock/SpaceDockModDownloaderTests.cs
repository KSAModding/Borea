using System.IO.Compression;
using System.Security.Cryptography;
using Borea.Core.Mods;
using Borea.Network.SpaceDock;

namespace Borea.Network.Tests;

public sealed class SpaceDockModDownloaderTests : IDisposable
{
    private readonly string _tempRoot;

    public SpaceDockModDownloaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    private static byte[] BuildZip(Dictionary<string, string> filesByRelativePath)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in filesByRelativePath)
            {
                var entry = archive.CreateEntry(path.Replace('\\', '/'));
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    private static HttpClient BuildClient(string modDetailJson, byte[] zipBytes, out FakeHttpMessageHandler handler) =>
        FakeHttpMessageHandler.BuildClient(request =>
            request.RequestUri!.AbsolutePath.Contains("/api/mod/")
                ? FakeHttpMessageHandler.JsonResponse(modDetailJson)
                : FakeHttpMessageHandler.ByteResponse(zipBytes),
            out handler);

    private const string ModDetailJson = """
        {"name":"Mod A (SpaceDock listing)","id":1,"author":"a","default_version_id":1,
         "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1,
                      "download_path":"/mod/1/ModA/download/v1.0.0"}]}
        """;

    [Fact]
    public async Task DownloadAsync_WrappedZip_FlattensIntoDestination()
    {
        var zip = BuildZip(new Dictionary<string, string>
        {
            ["Mod A/mod.toml"] = "name = \"ModA\"",
            ["Mod A/plugin.dll"] = "fake dll content",
        });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());
        var destination = Path.Combine(_tempRoot, "dest");

        await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), destination);

        Assert.True(File.Exists(Path.Combine(destination, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(destination, "plugin.dll")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Mod A"))); // Wrapper should not survive.
    }

    [Fact]
    public async Task DownloadAsync_UnwrappedZip_ExtractsDirectly()
    {
        var zip = BuildZip(new Dictionary<string, string>
        {
            ["mod.toml"] = "name = \"ModA\"",
            ["plugin.dll"] = "fake dll content",
        });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());
        var destination = Path.Combine(_tempRoot, "dest");

        await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), destination);

        Assert.True(File.Exists(Path.Combine(destination, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(destination, "plugin.dll")));
    }

    [Fact]
    public async Task DownloadAsync_WrappedZip_WithNestedSubfolders_PreservesStructureBelowWrapper()
    {
        var zip = BuildZip(new Dictionary<string, string>
        {
            ["Mod A/mod.toml"] = "name = \"ModA\"",
            ["Mod A/Plugins/plugin.dll"] = "fake dll content",
        });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());
        var destination = Path.Combine(_tempRoot, "dest");

        await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), destination);

        Assert.True(File.Exists(Path.Combine(destination, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(destination, "Plugins", "plugin.dll")));
    }

    [Fact]
    public async Task DownloadAsync_ReturnsCorrectChecksumAndByteCount()
    {
        var zip = BuildZip(new Dictionary<string, string> { ["mod.toml"] = "name = \"ModA\"" });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());

        var result = await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), Path.Combine(_tempRoot, "dest"));

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(zip));
        Assert.Equal(expectedChecksum, result.Checksum);
        Assert.Equal(zip.Length, result.BytesDownloaded);
    }

    [Fact]
    public async Task DownloadAsync_ReadsModTomlAndRegistersResolver()
    {
        var zip = BuildZip(new Dictionary<string, string>
        {
            ["Mod A/mod.toml"] = "name = \"ModMenu\"",
        });
        var client = BuildClient(ModDetailJson, zip, out _);
        var resolver = new SpaceDockResolver();
        var downloader = new SpaceDockModDownloader(client, resolver);

        var result = await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), Path.Combine(_tempRoot, "dest"));

        Assert.Equal("ModMenu", result.ModId); // From mod.toml, NOT the SpaceDock listing name.
        Assert.True(resolver.TryResolve("ModMenu", out var spaceDockId));
        Assert.Equal(1, spaceDockId);
    }

    [Fact]
    public async Task DownloadAsync_ReDownloadingLaterVersion_OverwritesResolverWithSameMapping()
    {
        var resolver = new SpaceDockResolver();
        resolver.Register("ModMenu", 1); // Simulates a prior version already registered.

        var zip = BuildZip(new Dictionary<string, string> { ["mod.toml"] = "name = \"ModMenu\"" });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, resolver);

        await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), Path.Combine(_tempRoot, "dest"));

        Assert.True(resolver.TryResolve("ModMenu", out var spaceDockId));
        Assert.Equal(1, spaceDockId);
    }

    [Fact]
    public async Task DownloadAsync_MissingModToml_ThrowsInvalidOperationException()
    {
        var zip = BuildZip(new Dictionary<string, string> { ["readme.txt"] = "oops, no mod.toml" });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), Path.Combine(_tempRoot, "dest")));
    }

    [Fact]
    public async Task DownloadAsync_VersionNotFound_ThrowsInvalidOperationException()
    {
        var client = BuildClient(ModDetailJson, Array.Empty<byte>(), out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("1", ModVersion.Parse("9.9.9"), Path.Combine(_tempRoot, "dest")));
    }

    [Fact]
    public async Task DownloadAsync_UnresolvableModId_ThrowsInvalidOperationException()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse("{}"), out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("never-registered-true-modid", ModVersion.Parse("1.0.0"), Path.Combine(_tempRoot, "dest")));
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgress()
    {
        var zip = BuildZip(new Dictionary<string, string> { ["mod.toml"] = "name = \"ModA\"" });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());
        var reports = new List<DownloadProgress>();

        await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), Path.Combine(_tempRoot, "dest"),
            progress: new Progress<DownloadProgress>(reports.Add));

        Assert.NotEmpty(reports);
        Assert.Equal(zip.Length, reports[^1].BytesDownloaded);
    }

    [Fact]
    public async Task DownloadAsync_CleansUpTempFilesAfterCompletion()
    {
        var zip = BuildZip(new Dictionary<string, string> { ["mod.toml"] = "name = \"ModA\"" });
        var client = BuildClient(ModDetailJson, zip, out _);
        var downloader = new SpaceDockModDownloader(client, new SpaceDockResolver());

        await downloader.DownloadAsync("1", ModVersion.Parse("1.0.0"), Path.Combine(_tempRoot, "dest"));

        var leftoverTempEntries = Directory.GetFileSystemEntries(Path.GetTempPath(), "borea-spacedock-*");
        Assert.Empty(leftoverTempEntries);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}