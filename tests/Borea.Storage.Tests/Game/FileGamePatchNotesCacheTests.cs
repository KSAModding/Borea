using System.Text;
using Borea.Core.Game;
using Borea.Storage.Game;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Game;

public sealed class FileGamePatchNotesCacheTests : IDisposable
{
    private const string FileName = "v2026.9.X.5438.json";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private FileGamePatchNotesCache Cache() => new(new TestGamePathProvider(_tempRoot));

    [Fact]
    public async Task WriteThenRead_ReturnsTheBytesFromTheGamePatchNotesFolder()
    {
        var bytes = Encoding.UTF8.GetBytes("""{ "build": "2026.9.10.5438" }""");

        await Cache().WriteAsync(FileName, bytes);

        Assert.Equal(bytes, await Cache().ReadAsync(FileName));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_tempRoot, "GamePatchNotes", FileName)));
        Assert.Equal([FileName], Directory.GetFiles(Path.Combine(_tempRoot, "GamePatchNotes")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ReadAsync_NothingStored_ReturnsNull()
    {
        Assert.Null(await Cache().ReadAsync(FileName));
    }

    [Fact]
    public async Task ReadAsync_FileAboveTheCap_ReturnsNull()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_tempRoot, "GamePatchNotes")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(folder, FileName), new byte[GamePatchNotesFile.MaxDownloadBytes + 1]);

        Assert.Null(await Cache().ReadAsync(FileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..json")]
    [InlineData("../index.json")]
    [InlineData("v2026.9.X.5438.txt")]
    public async Task NotAPublishedFileName_Throws(string fileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Cache().ReadAsync(fileName));
        await Assert.ThrowsAsync<ArgumentException>(() => Cache().WriteAsync(fileName, new byte[1]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
