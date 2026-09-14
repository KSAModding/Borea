using Borea.Storage.Launch;

namespace Borea.Storage.Tests.Launch;

public sealed class DotnetHostTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    public DotnetHostTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    private string PlaceFile(params string[] parts)
    {
        var path = Path.Combine(new[] { _tempRoot }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    [Fact]
    public void AssemblyBeside_ExeWithAssembly_ReturnsTheAssembly()
    {
        var assembly = PlaceFile("StarMap.dll");

        Assert.Equal(assembly, DotnetHost.AssemblyBeside(PlaceFile("StarMap.exe")));
    }

    [Fact]
    public void AssemblyBeside_ExeWithoutAssembly_ReturnsNull()
    {
        Assert.Null(DotnetHost.AssemblyBeside(PlaceFile("StarMap.exe")));
    }

    [Fact]
    public void AssemblyBeside_NotAnExe_ReturnsNull()
    {
        PlaceFile("StarMap.dll");

        Assert.Null(DotnetHost.AssemblyBeside(PlaceFile("StarMap")));
    }

    [Fact]
    public void Find_SkipsDirectoriesWithoutDotnet_AndReturnsTheFirstMatch()
    {
        var first = PlaceFile("first", "dotnet");
        var second = PlaceFile("second", "dotnet");
        var searchPath = string.Join(
            Path.PathSeparator,
            Path.Combine(_tempRoot, "empty"),
            Path.GetDirectoryName(first),
            Path.GetDirectoryName(second));

        Assert.Equal(first, DotnetHost.Find(searchPath));
    }

    [Fact]
    public void Find_RelativeDirectory_IsSkipped()
    {
        Assert.Null(DotnetHost.Find(Path.Combine("relative", "tools")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Find_NoSearchPath_ReturnsNull(string? searchPath)
    {
        Assert.Null(DotnetHost.Find(searchPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
