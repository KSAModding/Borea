using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Updates;
using Borea.Network.GitHub;
using Borea.Storage.Updates;

namespace Borea.Composition.Tests;

/// <summary>
/// The self-update through the real release downloads, when GitHub answers the download link of one
/// file with a server error and serves the same file through its API address.
/// </summary>
public sealed class SelfUpdateFallbackTests : IDisposable
{
    private const string ArchiveName = "Borea-0.2.0-win-x64.zip";

    private const string DownloadPrefix = "https://github.com/KSAModding/Borea/releases/download/v0.2.0/";

    private const string ApiPrefix = "https://api.github.com/repos/KSAModding/Borea/releases/assets/";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly string _folder;
    private readonly string _programPath;
    private readonly byte[] _archive = Zip("Borea-0.2.0-win-x64", "borea.exe", "the new build");
    private readonly ListLog _log = new();
    private readonly List<string> _requested = [];

    public SelfUpdateFallbackTests()
    {
        _folder = Path.Combine(_root, "Borea");
        _programPath = Path.Combine(_folder, "borea.exe");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(_programPath, "the running build");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static BoreaRelease Release() =>
        new(ModVersion.Parse("0.2.0"), "v0.2.0", "https://github.com/KSAModding/Borea/releases/tag/v0.2.0")
        {
            Assets =
            [
                new BoreaReleaseAsset(ArchiveName, DownloadPrefix + ArchiveName) { ApiUrl = ApiPrefix + "1" },
                new BoreaReleaseAsset(BoreaArchive.ChecksumsFileName, DownloadPrefix + BoreaArchive.ChecksumsFileName) { ApiUrl = ApiPrefix + "2" },
            ],
        };

    /// <summary>A self-updater whose release downloads go to <paramref name="answer"/>, which gets each requested URL.</summary>
    private FileSelfUpdater Updater(Func<string, HttpResponseMessage> answer)
    {
        var http = new HttpClient(new AnsweringHandler(url =>
        {
            _requested.Add(url);
            return answer(url);
        }));
        return new FileSelfUpdater(new BoreaReleaseFiles(http, _log), _log, BoreaProduct.App, programPath: _programPath, platform: "win-x64", isReleaseBuild: true);
    }

    private static string Checksums(byte[] archive) => $"{Convert.ToHexStringLower(SHA256.HashData(archive))}  {ArchiveName}\n";

    private static HttpResponseMessage Bytes(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    [Fact]
    public async Task StageAsync_AChecksumsLinkThatAnswers504_ReadsTheChecksumsFromTheApiAddressAndStagesTheBuild()
    {
        var updater = Updater(url => url switch
        {
            DownloadPrefix + BoreaArchive.ChecksumsFileName => Status(HttpStatusCode.GatewayTimeout),
            ApiPrefix + "2" => Bytes(Encoding.UTF8.GetBytes(Checksums(_archive))),
            DownloadPrefix + ArchiveName => Bytes(_archive),
            _ => Status(HttpStatusCode.NotFound),
        });

        var staged = await updater.StageAsync(Release());

        Assert.Equal(ModVersion.Parse("0.2.0"), staged.Version);
        Assert.Equal([DownloadPrefix + BoreaArchive.ChecksumsFileName, ApiPrefix + "2", DownloadPrefix + ArchiveName], _requested);
        Assert.Contains(_log.Lines, line => line.Contains($"{BoreaArchive.ChecksumsFileName} came from {ApiPrefix}2", StringComparison.Ordinal));
        Assert.Equal("the running build", File.ReadAllText(_programPath));
    }

    [Fact]
    public async Task StageAsync_AnArchiveFromTheApiAddressThatDoesNotMatchTheChecksums_StopsAndLeavesNothingBehind()
    {
        var updater = Updater(url => url switch
        {
            DownloadPrefix + BoreaArchive.ChecksumsFileName => Bytes(Encoding.UTF8.GetBytes(Checksums(_archive))),
            DownloadPrefix + ArchiveName => Status(HttpStatusCode.GatewayTimeout),
            ApiPrefix + "1" => Bytes(Zip("Borea-0.2.0-win-x64", "borea.exe", "other bytes")),
            _ => Status(HttpStatusCode.NotFound),
        });

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => updater.StageAsync(Release()));

        Assert.Equal(SelfUpdateFailure.Checksum, failure.Reason);
        Assert.Equal([DownloadPrefix + BoreaArchive.ChecksumsFileName, DownloadPrefix + ArchiveName, ApiPrefix + "1"], _requested);
        Assert.Empty(Directory.GetDirectories(_folder));
        Assert.Equal("the running build", File.ReadAllText(_programPath));
    }

    [Fact]
    public async Task StageAsync_BothAddressesOfTheChecksumsFail_StopsAsAFailedDownload()
    {
        var updater = Updater(url => url switch
        {
            DownloadPrefix + BoreaArchive.ChecksumsFileName => Status(HttpStatusCode.GatewayTimeout),
            ApiPrefix + "2" => Status(HttpStatusCode.ServiceUnavailable),
            _ => Bytes(_archive),
        });

        var failure = await Assert.ThrowsAsync<SelfUpdateFailedException>(() => updater.StageAsync(Release()));

        Assert.Equal(SelfUpdateFailure.Download, failure.Reason);
        Assert.Equal([DownloadPrefix + BoreaArchive.ChecksumsFileName, ApiPrefix + "2"], _requested);
    }

    /// <summary>A release zip that holds one folder with the program and the license in it.</summary>
    private static byte[] Zip(string folderName, string programName, string programContent)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, $"{folderName}/{programName}", programContent);
            Write(archive, $"{folderName}/LICENSE", "MIT");
        }

        return bytes.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var entry = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(entry);
        writer.Write(content);
    }

    private sealed class AnsweringHandler(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(answer(request.RequestUri!.AbsoluteUri));
    }

    private sealed class ListLog : IBoreaLog
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                    return _lines.ToArray();
            }
        }

        public string CurrentFilePath => "borea.log";

        public void Write(string message)
        {
            lock (_lines)
                _lines.Add(message);
        }

        public void Write(string message, Exception exception) => Write($"{message} {exception.Message}");

        public IReadOnlyList<string> ReadRecentLines(int maxLines) => Lines.TakeLast(maxLines).ToList();
    }
}
