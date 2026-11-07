using Borea.Storage.Files;

namespace Borea.Storage.Tests.Files;

public sealed class DriveFreeSpaceProbeTests : IDisposable
{
    private readonly string _root;

    public DriveFreeSpaceProbeTests()
    {
        // The temporary folder of macOS is itself reached through a link (/var to /private/var).
        _root = FileSystemPaths.ResolveLinks(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid())).FullName);
    }

    [Fact]
    public void MountPointOf_AFolderReachedThroughALink_IsTheMountOfTheLinkTarget()
    {
        // As on an atomic Linux system, where /home links into /var and only /var is a writable mount.
        var mounted = Directory.CreateDirectory(Path.Combine(_root, "var", "home")).FullName;
        var link = Path.Combine(_root, "home");
        Assert.True(new DirectoryLinker().TryCreate(link, mounted).Linked);
        var mountPoints = new[] { Path.GetPathRoot(_root)!, _root, mounted };

        var mountPoint = DriveFreeSpaceProbe.MountPointOf(Path.Combine(link, "player", "Borea"), mountPoints);

        Assert.Equal(mounted, mountPoint);
    }

    public void Dispose()
    {
        DirectoryLinks.DeleteTreeWithoutFollowingLinks(_root);
    }
}
