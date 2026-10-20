using Borea.Core.Mods;
using Borea.Core.Sharing;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Sharing;

public sealed class ContentShareProposalTests
{
    [Fact]
    public void Of_CorePartAndModPart_GivesOneRequiredEntryWithTheInstalledVersion()
    {
        var used = Ids((ContentIdKind.Part, "CoreCommandPod"), (ContentIdKind.Part, "RoverWheel"));
        var providers = Providers(Core("CoreCommandPod"), Mod("ExamplePartsMod", "RoverWheel"));

        var share = ContentShareProposal.Of(used, providers, [Installed("ExamplePartsMod", "1.4.0")]);

        var entry = Assert.Single(share.Dependencies);
        Assert.Equal(("ExamplePartsMod", "required", "1.4.0", (string?)null), (entry.Id, entry.Kind, entry.Min, entry.Max));
        Assert.Empty(share.Notes);
    }

    [Fact]
    public void Of_TwoModsDefineOnePart_GivesBothAndANoteNamingThemInLoadOrder()
    {
        var used = Ids((ContentIdKind.Part, "RoverWheel"));
        var providers = Providers(Mod("WheelsA", "RoverWheel"), Mod("WheelsB", "roverwheel"));

        var share = ContentShareProposal.Of(used, providers, []);

        Assert.Equal(["WheelsA", "WheelsB"], share.Dependencies.Select(entry => entry.Id));
        var note = Assert.Single(share.Notes);
        Assert.Equal(ContentShareNoteKind.SharedId, note.Kind);
        Assert.Equal(["WheelsA", "WheelsB"], note.Mods);
    }

    [Fact]
    public void Of_ModBoreaDidNotInstall_GivesAnEntryWithoutMin()
    {
        var share = ContentShareProposal.Of(Ids((ContentIdKind.Character, "Astro")), Providers(Characters("Kittens", "Astro")), [Installed("Other", "2.0.0")]);

        Assert.Null(Assert.Single(share.Dependencies).Min);
    }

    [Fact]
    public void Of_FolderNameThatIsNoId_IsNamedWithoutAnEntry()
    {
        var share = ContentShareProposal.Of(Ids((ContentIdKind.Part, "RoverWheel"), (ContentIdKind.Part, "RoverSeat")), Providers(Mod("My Parts", "RoverWheel", "RoverSeat")), []);

        Assert.Empty(share.Dependencies);
        var note = Assert.Single(share.Notes);
        Assert.Equal(ContentShareNoteKind.InvalidFolder, note.Kind);
        Assert.Equal(["My Parts"], note.Mods);
        Assert.Equal(["RoverWheel", "RoverSeat"], note.Ids.Select(id => id.Id));
    }

    [Fact]
    public void Of_IdThatNothingOrOnlyADisabledModDefines_IsNamed()
    {
        var used = Ids((ContentIdKind.SubstancePhase, "Unobtainium(l)"), (ContentIdKind.Part, "RoverWheel"));
        var providers = Providers(Mod("Wheels", "RoverWheel") with { Enabled = false });

        var share = ContentShareProposal.Of(used, providers, []);

        Assert.Empty(share.Dependencies);
        Assert.Collection(
            share.Notes,
            note => Assert.Equal((ContentShareNoteKind.NotProvided, "Unobtainium(l)"), (note.Kind, note.Ids.Single().Id)),
            note => Assert.Equal((ContentShareNoteKind.DisabledProvider, "Wheels"), (note.Kind, note.Mods.Single())));
    }

    [Fact]
    public void Of_ModThatRedefinesACorePart_GetsNoEntry()
    {
        var share = ContentShareProposal.Of(Ids((ContentIdKind.Part, "CoreCommandPod")), Providers(Core("CoreCommandPod"), Mod("PodOverhaul", "CoreCommandPod")), []);

        Assert.Empty(share.Dependencies);
        Assert.Empty(share.Notes);
    }

    [Fact]
    public void Of_UnreadableFileAndAModThatDefinesNothingUsed_GiveANoteAndNoEntry()
    {
        var providers = new ContentProviders([Mod("Unrelated", "OtherPart")], ["mods/Broken/Parts.xml"]);

        var share = ContentShareProposal.Of(Ids((ContentIdKind.Part, "CoreCommandPod")), providers, []);

        Assert.Empty(share.Dependencies);
        Assert.Contains(share.Notes, note => note.Kind == ContentShareNoteKind.UnreadableFile && note.Path == "mods/Broken/Parts.xml");
    }

    private static ContentIds Ids(params (ContentIdKind Kind, string Id)[] ids)
    {
        var set = new ContentIds();
        foreach (var (kind, id) in ids)
            set.Add(kind, id);
        return set;
    }

    private static ContentProviders Providers(params ContentProvider[] mods) => new(mods, []);

    private static ContentProvider Core(params string[] parts) => Mod("Core", parts) with { IsGameContent = true };

    private static ContentProvider Characters(string folder, string character) => Mod(folder) with { Defines = Ids((ContentIdKind.Character, character)) };

    private static ContentProvider Mod(string folder, params string[] parts) =>
        new(folder, true, false, Ids(parts.Select(part => (ContentIdKind.Part, part)).ToArray()));

    private static InstalledMod Installed(string modId, string version)
    {
        var release = new ModVersionMetadata(1, modId, ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, TestFixtures.SampleDownload(), 2048, []);
        return new InstalledMod(modId, release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, release);
    }
}
