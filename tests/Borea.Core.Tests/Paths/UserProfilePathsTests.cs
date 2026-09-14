using Borea.Core.Paths;

namespace Borea.Core.Tests.Paths;

public sealed class UserProfilePathsTests
{
    private static readonly string Profile = Path.Combine(Path.GetTempPath(), "Users", "Someone");
    private static readonly char Separator = Path.DirectorySeparatorChar;

    [Fact]
    public void Hide_ReplacesEveryPathUnderTheProfile()
    {
        var text = $"Moved '{Path.Combine(Profile, "a")}' to {Path.Combine(Profile, "b")}.";

        Assert.Equal($"Moved '~{Separator}a' to ~{Separator}b.", UserProfilePaths.Hide(text, Profile));
    }

    [Fact]
    public void Hide_TheProfileItselfAndATrailingSeparator_BecomeTilde()
    {
        Assert.Equal("~", UserProfilePaths.Hide(Profile, Profile));
        Assert.Equal("~" + Separator, UserProfilePaths.Hide(Profile + Separator, Profile + Separator));
    }

    [Fact]
    public void Hide_SiblingFolderAndOtherPaths_StayAsTheyAre()
    {
        var sibling = Profile + "2";
        var elsewhere = Path.Combine(Path.GetTempPath(), "Games", "StarMap");

        Assert.Equal(sibling, UserProfilePaths.Hide(sibling, Profile));
        Assert.Equal(elsewhere, UserProfilePaths.Hide(elsewhere, Profile));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hide_NoProfile_ReturnsTheText(string? profile)
    {
        Assert.Equal("C:\\Users\\Someone", UserProfilePaths.Hide("C:\\Users\\Someone", profile));
    }

    [Fact]
    public void Hide_DefaultProfile_RemovesTheUserProfileFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.DoesNotContain(profile + Separator, UserProfilePaths.Hide(Path.Combine(profile, "AppData")));
    }
}
