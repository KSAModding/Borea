using Borea.Core.Updates;

namespace Borea.Core.Tests.Updates;

public sealed class SelfUpdateHandoverTests
{
    private const string Program = "/opt/Borea-0.1.0-linux-x64/borea";

    [Fact]
    public void ToArguments_AndTake_AreTheTwoSidesOfOneHandover()
    {
        var handover = new SelfUpdateHandover(Program, 4242, "A1B2C3");
        var arguments = handover.ToArguments().ToArray();

        var taken = SelfUpdateHandover.Take(ref arguments);

        Assert.Equal(handover, taken);
        Assert.Empty(arguments);
    }

    [Fact]
    public void ToArguments_AndTake_CarryTheWayTheOldBuildRan()
    {
        var handover = new SelfUpdateHandover(Program, 4242, "A1B2C3", FromCommandLine: true);
        var arguments = handover.ToArguments().ToArray();

        var taken = SelfUpdateHandover.Take(ref arguments);

        Assert.Equal(handover, taken);
        Assert.True(taken!.FromCommandLine);
    }

    [Fact]
    public void NewToken_IsAnotherTokenEveryTime()
    {
        var token = SelfUpdateHandover.NewToken();

        Assert.NotEqual(token, SelfUpdateHandover.NewToken());
        Assert.Equal(32, token.Length);
    }

    [Fact]
    public void Take_LeavesTheArgumentsBehindTheHandover()
    {
        var arguments = new[] { SelfUpdateHandover.Option, "/opt/borea", "7", "A1B2C3", "app", "settings", "show" };

        var taken = SelfUpdateHandover.Take(ref arguments);

        Assert.NotNull(taken);
        Assert.Equal(["settings", "show"], arguments);
    }

    public static TheoryData<string[]> NoHandover =>
    [
        [],
        ["settings", "show"],
        [SelfUpdateHandover.Option, "/opt/borea"],
        [SelfUpdateHandover.Option, "/opt/borea", "7", "A1B2C3"],
        [SelfUpdateHandover.Option, "/opt/borea", "not a number", "A1B2C3", "app"],
        [SelfUpdateHandover.Option, "/opt/borea", "-7", "A1B2C3", "app"],
        [SelfUpdateHandover.Option, " ", "7", "A1B2C3", "app"],
        [SelfUpdateHandover.Option, "/opt/borea", "7", " ", "app"],
        [SelfUpdateHandover.Option, "/opt/borea", "7", "A1B2C3", "whatever"],
    ];

    [Theory]
    [MemberData(nameof(NoHandover))]
    public void Take_AnythingElse_IsNoHandoverAndKeepsTheArguments(string[] given)
    {
        var arguments = given;

        var taken = SelfUpdateHandover.Take(ref arguments);

        Assert.Null(taken);
        Assert.Same(given, arguments);
    }
}
