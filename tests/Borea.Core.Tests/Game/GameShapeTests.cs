using Borea.Core.Game;

namespace Borea.Core.Tests.Game;

public sealed class GameShapeTests
{
    private static GameShape Shape(params GameAssumptionResult[] results)
        => new(new InstalledGameVersion(VerifiedGameBuilds.Current.Newest, VerifiedGameBuilds.Current.Newest.ToString()), VerifiedGameBuilds.Current, results);

    private static GameAssumptionResult Result(GameAssumption assumption, GameAssumptionState state)
        => new(assumption, state, "detail");

    [Fact]
    public void VerifiedBuilds_ComeFromTheFileInTheRepository()
    {
        Assert.NotEmpty(VerifiedGameBuilds.Current.Builds);
        Assert.Equal(VerifiedGameBuilds.Current.Builds[^1], VerifiedGameBuilds.Current.Newest);
    }

    [Fact]
    public void IsPastNewest_OnlyABuildAboveTheNewestVerifiedOne()
    {
        var newest = VerifiedGameBuilds.Current.Newest;

        Assert.False(VerifiedGameBuilds.Current.IsPastNewest(newest));
        Assert.False(VerifiedGameBuilds.Current.IsPastNewest(new GameVersion(newest.Year, newest.Month, newest.BuildNumber, newest.Revision - 1)));
        Assert.True(VerifiedGameBuilds.Current.IsPastNewest(new GameVersion(newest.Year, newest.Month, newest.BuildNumber, newest.Revision + 1)));
    }

    [Fact]
    public void Status_EverythingHolds_OnAVerifiedBuild()
    {
        var shape = Shape(Result(GameAssumption.GameAssembly, GameAssumptionState.Holds));

        Assert.Equal(GameShapeStatus.Verified, shape.Status);
        Assert.True(shape.AllowsWrites);
    }

    [Fact]
    public void Status_ABrokenAssumption_StopsWrites()
    {
        var shape = Shape(
            Result(GameAssumption.GameAssembly, GameAssumptionState.Holds),
            Result(GameAssumption.ProfileManifest, GameAssumptionState.Broken));

        Assert.Equal(GameShapeStatus.Broken, shape.Status);
        Assert.False(shape.AllowsWrites);
        Assert.Equal(GameAssumption.ProfileManifest, Assert.Single(shape.Broken).Assumption);
    }

    [Fact]
    public void Status_NothingToCheck_IsUnknown()
    {
        var shape = new GameShape(null, VerifiedGameBuilds.Current, [Result(GameAssumption.GameAssembly, GameAssumptionState.NotChecked)]);

        Assert.Equal(GameShapeStatus.Unknown, shape.Status);
        Assert.True(shape.AllowsWrites);
        Assert.Equal("unknown", shape.BuildText);
    }

    [Fact]
    public void Status_ABuildPastTheNewestVerifiedOne_IsUntested()
    {
        var newest = VerifiedGameBuilds.Current.Newest;
        var newer = new GameVersion(newest.Year, newest.Month, newest.BuildNumber, newest.Revision + 1);
        var shape = new GameShape(
            new InstalledGameVersion(newer, newer.ToString()),
            VerifiedGameBuilds.Current,
            [Result(GameAssumption.GameAssembly, GameAssumptionState.Holds)]);

        Assert.Equal(GameShapeStatus.Untested, shape.Status);
        Assert.True(shape.AllowsWrites);
    }

    [Fact]
    public void BuildText_AVersionThatDidNotParse_KeepsTheRawString()
    {
        var shape = new GameShape(new InstalledGameVersion(null, "not a version"), VerifiedGameBuilds.Current, []);

        Assert.Equal("not a version", shape.BuildText);
    }

    [Fact]
    public void Subject_NoBuildFound_NamesTheInstallation()
    {
        var shape = new GameShape(null, VerifiedGameBuilds.Current, []);

        Assert.Equal("This KSA installation", shape.Subject);
    }

    [Fact]
    public void Subject_ABuild_NamesTheBuild()
    {
        var shape = new GameShape(new InstalledGameVersion(null, "not a version"), VerifiedGameBuilds.Current, []);

        Assert.Equal("KSA not a version", shape.Subject);
    }
}
