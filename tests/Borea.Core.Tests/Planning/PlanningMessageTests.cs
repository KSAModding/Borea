using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.Tests.Planning;

public sealed class PlanningMessageTests
{
    private static readonly ModDependency Library = new("library", ModDependencyKind.Required, ModVersion.Parse("1.0.0"));
    private static readonly ModDependency Alternatives = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("first"), new ModDependencyAlternative("second")]);

    public static TheoryData<PlanningMessage, string, string> Messages => new()
    {
        { new("A", PlanningMessageKind.ForeignOwned), "foreign-owned", "A managed install cannot replace foreign content." },
        { new("A", PlanningMessageKind.ExactPinConflict) { Version = ModVersion.Parse("1.0.0"), OtherVersion = ModVersion.Parse("2.0.0") }, "exact-pin-conflict", "Exact versions 1.0.0 and 2.0.0 were both requested." },
        { new("A", PlanningMessageKind.OutsideChannel) { Channel = ReleaseChannel.Testing }, "outside-channel", "No release of A in the testing channel is available." },
        { new("A", PlanningMessageKind.MissingRequest), "missing-request", "No release was selected for the request." },
        { new("A", PlanningMessageKind.ExactPin) { Version = ModVersion.Parse("1.0.0") }, "exact-pin", "Exact version 1.0.0 was not selected." },
        { new("A", PlanningMessageKind.ProposedConflict) { Dependency = Library }, "proposed-conflict", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.Yanked), "yanked", "The selected release is yanked." },
        { new("A", PlanningMessageKind.Yanked) { Value = "Broken build." }, "yanked", "Broken build." },
        { new("A", PlanningMessageKind.YankedPin), "yanked", "The exact pinned release is yanked." },
        { new("A", PlanningMessageKind.ReleaseChannel) { Version = ModVersion.Parse("2.0.0-dev.1"), Status = ReleaseStatus.Dev, Channel = ReleaseChannel.Stable }, "release-channel", "Release 2.0.0-dev.1 has the release status dev, which the stable channel does not offer." },
        { new("A", PlanningMessageKind.Incompatible) { Value = "2026.9.4.5400" }, "incompatible", "The release is incompatible with the target game." },
        { new("A", PlanningMessageKind.Compatibility) { Compatibility = GameCompatibility.Unknown }, "compatibility", "Game compatibility is Unknown." },
        { new("A", PlanningMessageKind.Platform) { Platform = OsPlatform.Linux }, "platform", "The release does not list Linux as a supported platform." },
        { new("A", PlanningMessageKind.PlatformUnknown) { Value = "amiga" }, "platform-unknown", "The release contains the unknown platform 'amiga'." },
        { new("A", PlanningMessageKind.UnknownDependency) { Dependency = Library }, "unknown-dependency", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.DependencyConflict) { Dependency = Library }, "dependency-conflict", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.ForeignConflictUnknown) { Dependency = Library }, "foreign-conflict-unknown", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.ForeignConflict) { Dependency = Library }, "foreign-conflict", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.ForeignVersionUnknown) { Dependency = Library }, "foreign-version-unknown", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.UnsatisfiedDependency) { Dependency = Library }, "unsatisfied-dependency", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.AlternativeChoice) { Dependency = Alternatives }, "alternative-choice", "Select one alternative for Required dependency on any of [first, second]." },
        { new("A", PlanningMessageKind.InvalidAlternative) { Dependency = Alternatives, Value = "third" }, "invalid-alternative", "'third' is not an available alternative." },
        { new("A", PlanningMessageKind.ForeignAlternativeUnknown) { Dependency = Alternatives, Value = "first" }, "foreign-alternative-unknown", "first" },
        { new("A", PlanningMessageKind.ForeignAlternativeUnknown) { Dependency = Alternatives }, "foreign-alternative-unknown", "Required dependency on any of [first, second]" },
        { new("A", PlanningMessageKind.UnsatisfiedAlternative) { Dependency = Alternatives, Value = "second" }, "unsatisfied-alternative", "second" },
        { new("A", PlanningMessageKind.RetainedAlternative) { Dependency = Alternatives }, "retained-alternative", "Required dependency on any of [first, second]" },
        { new("A", PlanningMessageKind.RetainedDependent) { Dependency = Library }, "retained-dependent", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.RetainedConflict) { Dependency = Library }, "retained-conflict", "Required dependency on mod 'library' >= 1.0.0" },
        { new("A", PlanningMessageKind.RetainedUnsatisfied) { Dependency = Library }, "retained-unsatisfied", "Required dependency on mod 'library' >= 1.0.0" },
        { new("planning", PlanningMessageKind.SearchLimit) { Limit = 100_000 }, "search-limit", "Planning exceeded the deterministic limit of 100000 candidate states." },
        { new("pack", PlanningMessageKind.RetractedPack), "retracted-pack", "The selected pack version is retracted." },
        { new("pack", PlanningMessageKind.RetractedPack) { Value = "Broken pack." }, "retracted-pack", "Broken pack." },
        { new("A", PlanningMessageKind.UnlistedPin), "unlisted-pin", "The exact pinned release is not listed and cannot be installed by Borea." },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void CodeAndMessage_AreTheTextTheCliPrints(PlanningMessage message, string code, string text)
    {
        Assert.Equal(code, message.Code);
        Assert.Equal(text, message.Message);
    }

    [Fact]
    public void Messages_CoverEveryKind()
    {
        var covered = Messages.Select(row => ((PlanningMessage)row[0]).Kind).ToHashSet();

        Assert.Equal(Enum.GetValues<PlanningMessageKind>().ToHashSet(), covered);
    }
}
