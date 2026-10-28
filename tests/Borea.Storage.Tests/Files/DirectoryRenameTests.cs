using Borea.Storage.Files;
using Borea.Storage.Tests.Launch;

namespace Borea.Storage.Tests.Files;

public sealed class DirectoryRenameTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid())).FullName;

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public void Move_WhileAProgramReadsAFileBelow_MovesOnceTheFileIsClosed()
    {
        var (source, target, file) = Folder();
        var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var pauses = 0;

        try
        {
            DirectoryRename.Move(source, target, delay =>
            {
                if (pauses++ == 0)
                    held.Dispose();
                else
                    Thread.Sleep(delay);
            });
        }
        finally
        {
            held.Dispose();
        }

        Assert.InRange(pauses, 1, DirectoryRename.Attempts - 1);
        Assert.True(File.Exists(Path.Combine(target, "mod.dll")));
        Assert.False(Directory.Exists(source));
    }

    [WindowsFact("Only Windows refuses to move a folder while a handle below it is open.")]
    public void Move_WhileTheFileStaysOpen_GivesUpAfterTheLastAttemptWithTheError()
    {
        var (source, target, file) = Folder();
        var pauses = 0;

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => DirectoryRename.Move(source, target, _ => pauses++));

        Assert.Equal(DirectoryRename.Attempts - 1, pauses);
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public void Move_ToAFolderThatExists_FailsAtOnce()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "Mod")).FullName;
        var pauses = 0;

        Assert.Throws<IOException>(() => DirectoryRename.Move(source, target, _ => pauses++));

        Assert.Equal(0, pauses);
    }

    private (string Source, string Target, string File) Folder()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var file = Path.Combine(source, "mod.dll");
        File.WriteAllText(file, "mod");
        return (source, Path.Combine(_root, "Mod"), file);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
