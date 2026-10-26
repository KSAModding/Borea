using Borea.Storage.Files;

namespace Borea.Storage.Tests.Files;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid())).FullName;

    [Fact]
    public async Task WriteAllTextAsync_WhileAReaderHoldsTheFile_WritesOnceTheReaderCloses()
    {
        var path = Path.Combine(_root, "settings.toml");
        await File.WriteAllTextAsync(path, "old");

        Task write;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            write = AtomicFile.WriteAllTextAsync(path, "new");
            await Task.Delay(100);
        }

        await write;
        Assert.Equal("new", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
