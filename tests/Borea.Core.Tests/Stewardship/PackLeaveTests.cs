using Borea.Core.GitHub;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class PackLeaveTests
{
    private static readonly PackVersionPins[] ThreeVersions =
    [
        new("1.10.0", ["mymod", "Other"]),
        new("1.2.0", ["Other"]),
        new("1.9.0", ["MyMod", "Other"]),
    ];

    [Theory]
    [InlineData("MyMod", "", "1.9.0,1.10.0", "", null)]
    [InlineData("MyMod", "1.9.0", "1.10.0", "1.9.0", null)]
    [InlineData("MyMod", "1.9.0,1.10.0", "", "1.9.0,1.10.0", IndexStatusRefusal.NotPinned)]
    [InlineData("Gone", "", "", "", IndexStatusRefusal.NotPinned)]
    public void Plan_RetractsTheVersionsThatPinTheModOldestFirst_LeavesOutTheRetractedOnes_AndRefusesAPackWithNoneLeft(string mod, string retractedBefore, string versions, string retracted, IndexStatusRefusal? refusal)
    {
        var entries = retractedBefore.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(version => new IndexStatusEntry("MY-PACK", IndexStatusEntry.Retracted, version, null, "Broken."))
            .Append(new IndexStatusEntry("other-pack", IndexStatusEntry.Retracted, "1.10.0", null, "Broken."))
            .ToList();

        var plan = new PackLeave("my-pack", mod, 12).Plan(ThreeVersions, entries);

        Assert.Equal(versions.Split(',', StringSplitOptions.RemoveEmptyEntries), plan.Versions);
        Assert.Equal(retracted.Split(',', StringSplitOptions.RemoveEmptyEntries), plan.Retracted);
        Assert.Equal(refusal, plan.Refusal);
    }

    [Theory]
    [InlineData(true, "1.9.0,1.10.0", "on versions `1.9.0`, `1.10.0` of `my-pack` in `index-status.toml`, because each of them pins", "@alice owns `MyMod` by the ownership proof of its listing.")]
    [InlineData(false, "1.10.0", "on version `1.10.0` of `my-pack` in `index-status.toml`, because it pins", "A steward confirmed by the forums account of its listing that @alice owns `MyMod`.")]
    public void Body_NamesTheVersionsHowTheStewardKnowsTheReporterAndTheReport_AndMentionsThePackOwner(bool proven, string versions, string sets, string ownership)
    {
        var leave = new PackLeave("my-pack", "MyMod", 12) { Reporter = new GitHubAccount("alice", 5), ConfirmedByForums = !proven };

        var body = leave.Body(versions.Split(','), proven, ["bob"]);

        Assert.Equal(
            $"Sets `retracted` {sets} `MyMod`.\n\n"
            + "Reason: The author of MyMod asked to take it out of this pack in https://github.com/KSAModding/content-index/issues/12\n\n"
            + $"{ownership}\n\nCloses #12\n\n@bob owns `my-pack`.",
            body);
    }
}
