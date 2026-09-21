using Borea.Storage.Files;

namespace Borea.Storage.Tests.Files;

public sealed class DirectoryLinksTests : IDisposable
{
    private readonly string _root;
    private readonly DirectoryLinker _linker = new();

    public DirectoryLinksTests()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid())).FullName;
    }

    [Fact]
    public void DeleteTreeWithoutFollowingLinks_ALinkBelowTheFolder_RemovesTheFolderAndKeepsTheTarget()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "mod.toml"), "name = \"Linked\"");
        var tree = Directory.CreateDirectory(Path.Combine(_root, "tree", "mods")).FullName;
        File.WriteAllText(Path.Combine(tree, "own.txt"), "mine");
        Assert.True(_linker.TryCreate(Path.Combine(tree, "link"), target).Linked);

        DirectoryLinks.DeleteTreeWithoutFollowingLinks(Path.Combine(_root, "tree"));

        Assert.False(Directory.Exists(Path.Combine(_root, "tree")));
        Assert.True(File.Exists(Path.Combine(target, "mod.toml")));
    }

    [Fact]
    public void DeleteTreeWithoutFollowingLinks_TheFolderIsItselfALink_RemovesOnlyTheLink()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "mod.toml"), "name = \"Linked\"");
        var link = Path.Combine(_root, "link");
        Assert.True(_linker.TryCreate(link, target).Linked);

        DirectoryLinks.DeleteTreeWithoutFollowingLinks(link);

        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "mod.toml")));
    }

    [Fact]
    public void DeleteTreeWithoutFollowingLinks_ABrokenLinkBelowTheFolder_RemovesTheFolder()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var tree = Directory.CreateDirectory(Path.Combine(_root, "tree")).FullName;
        Assert.True(_linker.TryCreate(Path.Combine(tree, "link"), target).Linked);
        Directory.Delete(target);

        DirectoryLinks.DeleteTreeWithoutFollowingLinks(tree);

        Assert.False(Directory.Exists(tree));
    }

    [Fact]
    public void DeleteTreeWithoutFollowingLinks_MissingFolder_DoesNothing()
    {
        DirectoryLinks.DeleteTreeWithoutFollowingLinks(Path.Combine(_root, "gone"));
    }

    public void Dispose() => DirectoryLinks.DeleteTreeWithoutFollowingLinks(_root);
}
