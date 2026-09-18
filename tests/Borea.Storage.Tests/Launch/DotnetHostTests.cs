using Borea.Core.Game;
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

        Assert.Equal(first, DotnetHost.Find(OsPlatform.Linux, searchPath, null, []));
    }

    [Fact]
    public void Find_RelativeDirectory_IsSkipped()
    {
        Assert.Null(DotnetHost.Find(OsPlatform.Linux, Path.Combine("relative", "tools"), null, []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Find_NoSearchPath_ReturnsNull(string? searchPath)
    {
        Assert.Null(DotnetHost.Find(OsPlatform.Linux, searchPath, null, []));
    }

    [Fact]
    public void Find_OnThePathAndInTheRoot_PrefersThePath()
    {
        var onPath = PlaceFile("path", "dotnet");
        var inRoot = PlaceFile("root", "dotnet");

        Assert.Equal(onPath, DotnetHost.Find(OsPlatform.Linux, Path.GetDirectoryName(onPath), Path.GetDirectoryName(inRoot), []));
    }

    [Fact]
    public void Find_NotOnThePath_UsesTheDotnetRoot()
    {
        var inRoot = PlaceFile("root", "dotnet");
        var fallback = PlaceFile("default", "dotnet");

        Assert.Equal(inRoot, DotnetHost.Find(OsPlatform.Linux, Path.Combine(_tempRoot, "empty"), Path.GetDirectoryName(inRoot), [Path.GetDirectoryName(fallback)!]));
    }

    [Fact]
    public void Find_NeitherOnThePathNorInTheRoot_UsesTheDefaultDirectory()
    {
        var fallback = PlaceFile("default", "dotnet");

        Assert.Equal(fallback, DotnetHost.Find(OsPlatform.MacOs, null, Path.Combine(_tempRoot, "root"), [Path.Combine(_tempRoot, "missing"), Path.GetDirectoryName(fallback)!]));
    }

    [Fact]
    public void Find_OnWindows_LooksForTheExe()
    {
        PlaceFile("tools", "dotnet");
        var host = PlaceFile("tools", "dotnet.exe");

        Assert.Equal(host, DotnetHost.Find(OsPlatform.Windows, Path.GetDirectoryName(host), null, []));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
