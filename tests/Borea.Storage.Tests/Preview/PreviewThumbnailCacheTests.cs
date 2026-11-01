using Borea.Storage.Preview;
using Borea.Storage.Tests.Logging;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Preview;

public sealed class PreviewThumbnailCacheTests : IDisposable
{
    private const string Build = "2026.10.19.5638";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public PreviewThumbnailCacheTests() => _paths = new TestGamePathProvider(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Thumbs => Path.Combine(_paths.GetPreviewCacheFolder(), "thumbs");

    [Fact]
    public void TheSceneKey_OfAStoredStill_IsRead_AndOtherArgumentsGiveNone()
    {
        var cache = new PreviewThumbnailCache(_paths, new RecordingLog());
        var sha = new string('A', 64);
        Assert.Null(cache.TryGetSceneKey(sha, Build));

        cache.Store(sha, Build, "scene1m", [1, 2, 3]);

        Assert.Equal("scene1m", cache.TryGetSceneKey(sha, Build));
        Assert.Null(cache.TryGetSceneKey(sha, "2026.10.13.5602"));
        Assert.Null(cache.TryGetSceneKey(null, Build));
    }

    [Fact]
    public void AStill_IsFoundByHashAndBuild_AnotherBuildMisses_AndANewSceneKeyReplacesIt()
    {
        var cache = new PreviewThumbnailCache(_paths, new RecordingLog());
        var sha = new string('A', 64);
        Assert.Null(cache.TryGetPath(sha, Build));
        Assert.True(cache.NeedsUpdate(sha, Build, "scene1"));
        Assert.False(Directory.Exists(_root));

        cache.Store(sha, Build, "scene1", [1, 2, 3]);

        var path = Path.Combine(Thumbs, new string('a', 64) + "-" + Build + ".png");
        Assert.Equal(path, cache.TryGetPath(sha, Build));
        Assert.Equal(path, cache.TryGetPath(sha.ToLowerInvariant(), Build));
        Assert.Null(cache.TryGetPath(sha, "2026.10.13.5602"));
        Assert.Null(cache.TryGetPath(null, Build));
        Assert.Null(cache.TryGetPath(sha, "../2026"));
        Assert.False(cache.NeedsUpdate(sha, Build, "scene1"));
        Assert.True(cache.NeedsUpdate(sha, Build, "scene2"));
        Assert.True(cache.NeedsUpdate(sha, "2026.10.13.5602", "scene1"));

        cache.Store(sha, Build, "scene2", [4, 5]);

        Assert.Equal([4, 5], File.ReadAllBytes(path));
        Assert.False(cache.NeedsUpdate(sha, Build, "scene2"));
        Assert.True(cache.NeedsUpdate(sha, Build, "scene1"));
        Assert.Equal([Path.ChangeExtension(path, ".key"), path], Directory.GetFiles(Thumbs).Order());
    }

    [Fact]
    public void Eviction_KeepsTheBound_RemovesTheOldestStillWithItsKey_AndSweepRemovesStaleParts()
    {
        // Each still is 100 bytes plus a key of 6 bytes, so two of them fit and three do not.
        var cache = new PreviewThumbnailCache(_paths, new RecordingLog(), capacityBytes: 250);
        var hashes = new[] { new string('1', 64), new string('2', 64), new string('3', 64) };
        for (var i = 0; i < hashes.Length; i++)
        {
            cache.Store(hashes[i], Build, "scene1", new byte[100]);
            File.SetLastWriteTimeUtc(cache.TryGetPath(hashes[i], Build)!, DateTime.UtcNow.AddHours(i - 10));
        }

        Assert.Null(cache.TryGetPath(hashes[0], Build));
        Assert.NotNull(cache.TryGetPath(hashes[1], Build));
        Assert.NotNull(cache.TryGetPath(hashes[2], Build));
        Assert.Equal(4, Directory.GetFiles(Thumbs).Length);
        Assert.True(Directory.GetFiles(Thumbs).Sum(file => new FileInfo(file).Length) <= 250);

        var stale = Path.Combine(Thumbs, "x.png.old.part");
        var fresh = Path.Combine(Thumbs, "x.png.new.part");
        File.WriteAllBytes(stale, [1]);
        File.WriteAllBytes(fresh, [1]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-25));

        Assert.Equal(1, cache.Sweep());
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }
}
