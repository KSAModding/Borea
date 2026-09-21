using Borea.Storage.Files;

namespace Borea.Storage.Tests.Files;

public sealed class DirectoryLinkerTests : IDisposable
{
    private readonly string _root;
    private readonly DirectoryLinker _linker = new();

    public DirectoryLinkerTests()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid())).FullName;
    }

    [Fact]
    public void TryCreate_ThenTheFilesBelowTheTargetAreFoundThroughTheLink()
    {
        var target = Target("mod.toml", "assets/part.json");
        var link = Path.Combine(_root, "link");

        var result = _linker.TryCreate(link, target);

        Assert.True(result.Linked, result.Reason);
        Assert.Null(result.Reason);
        Assert.True(File.Exists(Path.Combine(link, "mod.toml")));
        Assert.True(File.Exists(Path.Combine(link, "assets", "part.json")));
        Assert.True(_linker.IsLink(link));
        Assert.Equal(target, _linker.GetTarget(link));
    }

    [Fact]
    public void TryCreate_MissingTarget_ReportsItWithoutThrowing()
    {
        var result = _linker.TryCreate(Path.Combine(_root, "link"), Path.Combine(_root, "gone"));

        Assert.False(result.Linked);
        Assert.NotNull(result.Reason);
        Assert.False(Directory.Exists(Path.Combine(_root, "link")));
    }

    [Fact]
    public void TryCreate_TargetIsAFile_ReportsItWithoutThrowing()
    {
        var file = Path.Combine(_root, "release.zip");
        File.WriteAllText(file, "not a folder");

        var result = _linker.TryCreate(Path.Combine(_root, "link"), file);

        Assert.False(result.Linked);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void TryCreate_LinkPathInUse_ReportsItAndKeepsWhatIsThere()
    {
        var target = Target("mod.toml");
        var link = Directory.CreateDirectory(Path.Combine(_root, "link")).FullName;
        File.WriteAllText(Path.Combine(link, "own.txt"), "mine");

        var result = _linker.TryCreate(link, target);

        Assert.False(result.Linked);
        Assert.True(File.Exists(Path.Combine(link, "own.txt")));
        Assert.False(_linker.IsLink(link));
    }

    [Fact]
    public void Remove_LeavesTheTargetAndItsFiles()
    {
        var target = Target("mod.toml");
        var link = Path.Combine(_root, "link");
        Assert.True(_linker.TryCreate(link, target).Linked);

        _linker.Remove(link);

        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "mod.toml")));
    }

    [Fact]
    public void Remove_AFolderOfItsOwn_Throws()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;

        Assert.Throws<InvalidOperationException>(() => _linker.Remove(folder));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Remove_MissingPath_DoesNothing()
    {
        _linker.Remove(Path.Combine(_root, "gone"));
    }

    [Fact]
    public void IsLink_AndGetTarget_AFolderOfItsOwn_ReportNoLink()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;

        Assert.False(_linker.IsLink(folder));
        Assert.Null(_linker.GetTarget(folder));
    }

    [SecondVolumeFact("Only Windows uses a junction, which is the link that can cross a volume.")]
    public void TryCreate_TargetOnAnotherVolume_LinksWithoutElevation()
    {
        var target = Directory.CreateDirectory(Path.Combine(SecondVolume.Folder!, "BoreaTest_" + Guid.NewGuid())).FullName;
        try
        {
            File.WriteAllText(Path.Combine(target, "mod.toml"), "name = \"Linked\"");
            var link = Path.Combine(_root, "link");

            var result = _linker.TryCreate(link, target);

            Assert.True(result.Linked, result.Reason);
            Assert.Equal(target, _linker.GetTarget(link));
            Assert.True(File.Exists(Path.Combine(link, "mod.toml")));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void ALinkWhoseTargetIsGone_IsStillALinkAndIsRemovedAsOne()
    {
        var target = Target("mod.toml");
        var link = Path.Combine(_root, "link");
        Assert.True(_linker.TryCreate(link, target).Linked);

        Directory.Delete(target, recursive: true);

        Assert.True(_linker.IsLink(link));
        Assert.Equal(target, _linker.GetTarget(link));

        _linker.Remove(link);

        Assert.False(_linker.IsLink(link));
        Assert.False(Directory.Exists(link));
    }

    private string Target(params string[] files)
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        foreach (var file in files)
        {
            var path = Path.Combine(target, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }

        return target;
    }

    public void Dispose() => DirectoryLinks.DeleteTreeWithoutFollowingLinks(_root);
}
