using Borea.Storage.Toml;

namespace Borea.Storage.Tests.Toml;

public sealed class TomlFileStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public TomlFileStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    }

    private sealed class SampleDto
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        var path = Path.Combine(_tempRoot, "missing.toml");

        var result = await TomlFileStore.ReadAsync<SampleDto>(path);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_FileThatIsNotToml_ThrowsNamingThePath()
    {
        var path = Path.Combine(_tempRoot, "broken.toml");
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(path, "Name = \n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TomlFileStore.ReadAsync<SampleDto>(path));

        Assert.Contains(path, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task WriteAsync_CreatesContainingDirectory()
    {
        var path = Path.Combine(_tempRoot, "nested", "deeper", "file.toml");

        await TomlFileStore.WriteAsync(path, new SampleDto { Name = "test", Count = 1 });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsValues()
    {
        var path = Path.Combine(_tempRoot, "roundtrip.toml");
        var original = new SampleDto { Name = "hello", Count = 42 };

        await TomlFileStore.WriteAsync(path, original);
        var reloaded = await TomlFileStore.ReadAsync<SampleDto>(path);

        Assert.NotNull(reloaded);
        Assert.Equal("hello", reloaded!.Name);
        Assert.Equal(42, reloaded.Count);
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempRoot, "overwrite.toml");

        await TomlFileStore.WriteAsync(path, new SampleDto { Name = "first", Count = 1 });
        await TomlFileStore.WriteAsync(path, new SampleDto { Name = "second", Count = 2 });
        var reloaded = await TomlFileStore.ReadAsync<SampleDto>(path);

        Assert.Equal("second", reloaded!.Name);
    }

    [Fact]
    public void DeleteIfExists_IsIdempotent()
    {
        var path = Path.Combine(_tempRoot, "never-existed.toml");

        // Should not throw even though the file was never created.
        TomlFileStore.DeleteIfExists(path);
    }

    [Fact]
    public async Task DeleteIfExists_RemovesFile()
    {
        var path = Path.Combine(_tempRoot, "to-delete.toml");
        await TomlFileStore.WriteAsync(path, new SampleDto());

        TomlFileStore.DeleteIfExists(path);

        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
