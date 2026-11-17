using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class WindowServicesTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "borea-export-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task WriteLocalExportAsync_WriteFails_KeepsTheDestination()
    {
        var path = Path.Combine(_folder, "archive.zip");
        await File.WriteAllTextAsync(path, "previous archive");

        await Assert.ThrowsAsync<IOException>(() => WindowServices.WriteLocalExportAsync(path, async stream =>
        {
            await stream.WriteAsync(new byte[] { 1, 2, 3 });
            throw new IOException("Disk full");
        }));

        Assert.Equal("previous archive", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task WriteLocalExportAsync_WriteCompletes_ReplacesTheDestination()
    {
        var path = Path.Combine(_folder, "archive.zip");
        await File.WriteAllTextAsync(path, "previous archive");

        await WindowServices.WriteLocalExportAsync(path, stream => stream.WriteAsync(new byte[] { 1, 2, 3 }).AsTask());

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
