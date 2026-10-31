using Borea.Core.Game;
using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class GameExecutableTests
{
    private static readonly string GameDirectory = Path.Combine(Path.GetTempPath(), "BoreaTest", "Game");

    [Theory]
    [InlineData(OsPlatform.Windows, "KSA.exe")]
    [InlineData(OsPlatform.Linux, "KSA")]
    [InlineData(OsPlatform.MacOs, null)]
    public void FileName_Platform_IsTheGameFileOrNullWhenNoneIsKnown(OsPlatform platform, string? expected)
    {
        Assert.Equal(expected, GameExecutable.FileName(platform));
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

    [Fact]
    public void Plan_WithArguments_PassesEachAsItIs()
    {
        var plan = GameExecutable.Plan(GameDirectory, "KSA.exe", ["-windowed", "a b"]);

        Assert.Equal(new[] { "-windowed", "a b" }, plan.Arguments);
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
