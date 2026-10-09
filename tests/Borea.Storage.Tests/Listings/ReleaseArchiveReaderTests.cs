using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.Core.Mods;
using Borea.Core.Stewardship;
using Borea.Storage.Listings;
using Borea.Storage.Tests.Mods;

namespace Borea.Storage.Tests.Listings;

/// <summary>What the mod.toml of a stamped release archive declares, read as hosts.stamped_dependencies of content-index-releases reads it.</summary>
public sealed class ReleaseArchiveReaderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BoreaReleaseArchive_" + Guid.NewGuid().ToString("N"));

    public ReleaseArchiveReaderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task DeclaredDependencies_AreThoseOfTheModTomlAtTheStampedRoot_FromTheUrlOrAMirror_AndAreReadOnce()
    {
        var bytes = TestArchives.Build(
            ("ShaderExtensions/mod.toml", "name = \"ShaderExtensions\"\n\n[[StarMap.ModDependencies]]\nModId = \" KittenExtensionsContinued \"\n\n[[StarMap.ModDependencies]]\nModId = \"Extra\"\nOptional = true\n"),
            ("mod.toml", "[[StarMap.ModDependencies]]\nModId = \"AtTheRoot\"\n"));
        ModVersionMetadata? asked = null;
        var downloader = new FakeModDownloader
        {
            Bytes = bytes,
            Downloading = (release, _) =>
            {
                asked = release;
                return Task.CompletedTask;
            },
        };
        var reader = new ReleaseArchiveReader(downloader, _folder, 1000);

        var declared = await reader.DeclaredDependenciesAsync(Stamped(bytes, "ShaderExtensions"));
        var again = await reader.DeclaredDependenciesAsync(Stamped(bytes, "ShaderExtensions"));
        var atTheRoot = await reader.DeclaredDependenciesAsync(Stamped(bytes, string.Empty));

        Assert.Equal([new LocalModDependency("KittenExtensionsContinued", false), new LocalModDependency("Extra", true)], declared);
        Assert.Same(declared, again);
        Assert.Equal([new LocalModDependency("AtTheRoot", false)], atTheRoot);
        Assert.Equal(2, downloader.ArchivePaths.Count);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Equal(("https://example.com/ShaderExtensions.zip", Convert.ToHexString(SHA256.HashData(bytes))), (asked!.Download.Url, asked.Download.Sha256));
        Assert.Equal(["https://spacedock.info/mod/1/ShaderExtensions/download/1.0.5"], asked.Download.Mirrors);
    }

    [Fact]
    public async Task AReleaseThatIsNoMod_OrAnArchiveWithoutAModTomlThere_DeclaresNothing()
    {
        var bytes = TestArchives.Build(("ShaderExtensions/mod.toml", "[[StarMap.ModDependencies]]\nModId = \"Lib\"\n"));
        var reader = new ReleaseArchiveReader(new FakeModDownloader { Bytes = bytes }, _folder, 1000);

        Assert.Empty(await reader.DeclaredDependenciesAsync(Stamped(bytes, "ShaderExtensions", type: "tool")));
        Assert.Empty(await reader.DeclaredDependenciesAsync(Stamped(bytes, "Other")));
    }

    [Fact]
    public async Task OtherBytes_AFailedDownload_OrNoSha256_CannotBeRead()
    {
        var bytes = TestArchives.Build(("ShaderExtensions/mod.toml", "name = \"ShaderExtensions\"\n"));
        var other = new ReleaseArchiveReader(new FakeModDownloader { Bytes = TestArchives.Build(("Other/mod.toml", "x = 1\n")) }, _folder, 1000);
        var failing = new FakeModDownloader { Failure = new DownloadFailedException("No source served the archive.") };
        var withoutSha = Stamped(bytes, "ShaderExtensions");
        withoutSha = withoutSha.Replace(Convert.ToHexString(SHA256.HashData(bytes)), string.Empty, StringComparison.Ordinal);

        var changed = await Assert.ThrowsAsync<ReleaseArchiveException>(() => other.DeclaredDependenciesAsync(Stamped(bytes, "ShaderExtensions")));
        var failed = await Assert.ThrowsAsync<ReleaseArchiveException>(() => new ReleaseArchiveReader(failing, _folder, 1000).DeclaredDependenciesAsync(Stamped(bytes, "ShaderExtensions")));
        var unhashed = await Assert.ThrowsAsync<ReleaseArchiveException>(() => new ReleaseArchiveReader(failing, _folder, 1000).DeclaredDependenciesAsync(withoutSha));

        Assert.Equal("the archive at https://example.com/ShaderExtensions.zip no longer matches the stamped sha256", changed.Message);
        Assert.Equal("No source served the archive.", failed.Message);
        Assert.Equal("the release file names no sha256, so no archive matches it", unhashed.Message);
        Assert.Single(failing.ArchivePaths);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task AnArchiveLargerThanTheReleaseStates_StopsAndIsDeleted()
    {
        var bytes = new byte[600];
        var reader = new ReleaseArchiveReader(new FakeModDownloader { Bytes = bytes }, _folder, 1000);
        var stamped = JsonNode.Parse(Stamped(bytes, "ShaderExtensions"))!;
        stamped["download"]!["size"] = 500;

        var tooLarge = await Assert.ThrowsAsync<ReleaseArchiveException>(() => reader.DeclaredDependenciesAsync(stamped.ToJsonString()));
        stamped["download"]!["size"] = 2000;
        var aboveTheLimit = await Assert.ThrowsAsync<ReleaseArchiveException>(() => reader.DeclaredDependenciesAsync(stamped.ToJsonString()));

        Assert.Equal("the archive sends more than the 500 bytes that the release states", tooLarge.Message);
        Assert.Equal("the archive is 2000 bytes, above the limit of 1000 bytes", aboveTheLimit.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    /// <summary>A stamped release file of ShaderExtensions 1.0.5 whose archive is <paramref name="bytes"/>.</summary>
    private static string Stamped(byte[] bytes, string root, string type = "mod") => new JsonObject
    {
        ["spec_version"] = 1,
        ["id"] = "ShaderExtensions",
        ["type"] = type,
        ["version"] = "1.0.5",
        ["install"] = new JsonObject { ["root"] = root, ["derived"] = true },
        ["download"] = new JsonObject
        {
            ["url"] = "https://example.com/ShaderExtensions.zip",
            ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)),
            ["size"] = bytes.Length,
            ["content_type"] = "application/zip",
            ["mirrors"] = new JsonArray("https://spacedock.info/mod/1/ShaderExtensions/download/1.0.5"),
        },
        ["dependencies"] = new JsonArray(new JsonObject { ["id"] = "KittenExtensionsContinued", ["kind"] = "required", ["min"] = "0.5.2", ["source"] = "authored" }),
    }.ToJsonString();
}
