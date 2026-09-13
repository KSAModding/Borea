using Borea.Core.Game;
using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class GameExecutableTests
{
    private static readonly string GameDirectory = Path.Combine(Path.GetTempPath(), "BoreaTest", "Game");

    [Fact]
    public void FileName_Windows_IsKsaExe()
    {
        Assert.Equal("KSA.exe", GameExecutable.FileName(OsPlatform.Windows));
    }

    [Fact]
    public void FileName_Linux_IsKsa()
    {
        Assert.Equal("KSA", GameExecutable.FileName(OsPlatform.Linux));
    }

    [Fact]
    public void FileName_MacOs_IsNull()
    {
        Assert.Null(GameExecutable.FileName(OsPlatform.MacOs));
    }

    [Fact]
    public void Plan_StartsTheFileInTheGameDirectory_WithNothingAdded()
    {
        var plan = GameExecutable.Plan(GameDirectory, "KSA.exe");

        Assert.Equal(Path.Combine(GameDirectory, "KSA.exe"), plan.Executable);
        Assert.Equal(GameDirectory, plan.WorkingDirectory);
        Assert.Empty(plan.Arguments);
        Assert.Empty(plan.EnvironmentVariables);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bin/KSA.exe")]
    public void Plan_FileNameThatIsNotPlain_Throws(string fileName)
    {
        Assert.Throws<ArgumentException>(() => GameExecutable.Plan(GameDirectory, fileName));
    }

    [Fact]
    public void Plan_RelativeGameDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() => GameExecutable.Plan("Game", "KSA.exe"));
    }
}
