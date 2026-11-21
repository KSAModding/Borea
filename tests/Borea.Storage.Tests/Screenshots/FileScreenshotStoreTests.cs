using Borea.Core.Screenshots;
using Borea.Storage.Screenshots;

namespace Borea.Storage.Tests.Screenshots;

public sealed class FileScreenshotStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FileScreenshotStore _store = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ListAsync_MissingFolder_ReturnsNothing()
    {
        Assert.Empty(await _store.ListAsync(Path.Combine(_tempRoot, "missing")));
    }

    [Fact]
    public async Task ListAsync_MixedFolder_ReturnsImagesInTheFolderNewestFirst()
    {
        var folder = Path.Combine(_tempRoot, "shots");
        Write(Path.Combine(folder, "ksa_20261117_100000_1920x1080.png"), 10);
        Write(Path.Combine(folder, "ksa_20261118_100000_3840x2160.PNG"), 20);
        Write(Path.Combine(folder, "b.jpg"), 30, new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Local));
        Write(Path.Combine(folder, "a.jpeg"), 40, new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Local));
        Write(Path.Combine(folder, "notes.txt"), 5);
        Write(Path.Combine(folder, "old", "ksa_20261201_100000_1920x1080.png"), 5);

        var screenshots = await _store.ListAsync(folder);

        Assert.Equal(
            ["ksa_20261118_100000_3840x2160.PNG", "a.jpeg", "b.jpg", "ksa_20261117_100000_1920x1080.png"],
            screenshots.Select(screenshot => Path.GetFileName(screenshot.Path)));
        Assert.Equal([20L, 40L, 30L, 10L], screenshots.Select(screenshot => screenshot.SizeBytes));
        Assert.Equal(3840, screenshots[0].Width);
    }

    [Fact]
    public async Task CopyAsync_TakenName_CreatesTheFolderAndNumbersTheCopy()
    {
        var source = Path.Combine(_tempRoot, "source", "ksa_20261117_100000_1920x1080.png");
        var writtenAt = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Local);
        Write(source, 10, writtenAt);
        var target = Path.Combine(_tempRoot, "target", "deeper");
        Write(Path.Combine(target, "ksa_20261117_100000_1920x1080.png"), 99);
        var screenshot = Assert.Single(await _store.ListAsync(Path.GetDirectoryName(source)!));

        var first = await _store.CopyAsync([screenshot], target);
        var second = await _store.CopyAsync([screenshot], target);

        Assert.Equal(new ScreenshotCopyResult(target, 1), first);
        Assert.Equal(1, second.Copied);
        Assert.Equal(99, new FileInfo(Path.Combine(target, "ksa_20261117_100000_1920x1080.png")).Length);
        var numbered = new FileInfo(Path.Combine(target, "ksa_20261117_100000_1920x1080 (2).png"));
        Assert.Equal(10, numbered.Length);
        Assert.Equal(writtenAt, numbered.LastWriteTime);
        Assert.Equal(10, new FileInfo(Path.Combine(target, "ksa_20261117_100000_1920x1080 (3).png")).Length);
        Assert.True(File.Exists(source));
    }

    private static void Write(string path, int length, DateTime? lastWrite = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
        if (lastWrite is { } time)
            File.SetLastWriteTime(path, time);
    }
}
