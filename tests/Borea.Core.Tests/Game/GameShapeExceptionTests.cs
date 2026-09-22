using Borea.Core.Game;

namespace Borea.Core.Tests.Game;

public sealed class GameShapeExceptionTests
{
    [Fact]
    public void Message_NoBuildFound_NamesTheInstallation()
    {
        var shape = new GameShape(null, VerifiedGameBuilds.Current, []);

        Assert.StartsWith("This KSA installation does not have the shape", new GameShapeException(shape).Message);
    }
}
