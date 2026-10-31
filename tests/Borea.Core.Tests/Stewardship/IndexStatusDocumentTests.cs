using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class IndexStatusDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 18, 30, 5, TimeSpan.FromHours(2));

    private static readonly IndexContents Contents = IndexContents.FromPaths(
    [
        "README.md",
        "listings/MyMod.toml",
        "listings/Other.toml",
        "listings/README.md",
        "packs/my-pack/1.0.0.toml",
        "packs/my-pack/1.1.0.toml",
        "packs/my-pack/owner.json",
        "schemas/index-status.schema.json",
    ]);

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_TheCurrentFile_KeepsItByteForByte(string newLine)
    {
        var text = IndexStatusFixtures.Current.ReplaceLineEndings(newLine);

        var document = IndexStatusDocument.Parse(text);

        Assert.Equal(text, document.Text);
        Assert.Empty(document.Entries);
    }

    [Fact]
    public void Apply_FirstEntry_ReplacesTheEmptyEntriesLineAndKeepsTheHeader()
    {
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        var changed = document.Apply(IndexStatusChange.Delist("MyMod", "  The author asked for it.  "), Contents, Now);

        Assert.Equal(
            IndexStatusFixtures.Header + """
                [[entries]]
                id = "MyMod"
                state = "delisted"
                since = "2026-09-25T16:30:05Z"
                reason = "The author asked for it."

                """.ReplaceLineEndings("\n"),
            changed.Text);
        Assert.Equal([new IndexStatusEntry("MyMod", "delisted", null, "2026-09-25T16:30:05Z", "The author asked for it.")], changed.Entries);
    }

    [Fact]
    public void Apply_SecondEntry_FollowsTheFirstAfterABlankLine_InTheNewLinesOfTheFile()
    {
        var first = IndexStatusDocument.Parse(IndexStatusFixtures.Current.ReplaceLineEndings("\r\n"))
            .Apply(IndexStatusChange.Dispute("MyMod", "Two authors claim the id."), Contents, Now);

        var second = first.Apply(IndexStatusChange.Retract("my-pack", "1.0.0", "It installs a broken build."), Contents, Now);

        Assert.Equal(
            first.Text + "\r\n" + string.Join("\r\n", "[[entries]]", "id = \"my-pack\"", "state = \"retracted\"", "version = \"1.0.0\"", "since = \"2026-09-25T16:30:05Z\"", "reason = \"It installs a broken build.\"") + "\r\n",
            second.Text);
        Assert.Equal(["MyMod", "my-pack"], second.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void Apply_LiftOfEachEntry_LeavesTheOtherAsItWasWritten()
    {
        var one = IndexStatusDocument.Parse(IndexStatusFixtures.Current).Apply(IndexStatusChange.Dispute("MyMod", "Claimed twice."), Contents, Now);
        var two = one.Apply(IndexStatusChange.Delist("Other", "Taken down."), Contents, Now);
        var onlyOther = IndexStatusDocument.Parse(IndexStatusFixtures.Current).Apply(IndexStatusChange.Delist("Other", "Taken down."), Contents, Now);

        Assert.Equal(one.Text, two.Apply(IndexStatusChange.Lift(two.Entries[1], "Resolved."), Contents, Now).Text);
        Assert.Equal(onlyOther.Text, two.Apply(IndexStatusChange.Lift(two.Entries[0], "Resolved."), Contents, Now).Text);
    }

    [Fact]
    public void Apply_LiftOfTheLastEntry_PutsTheEmptyEntriesLineBack()
    {
        var delisted = IndexStatusDocument.Parse(IndexStatusFixtures.Current).Apply(IndexStatusChange.Delist("MyMod", "Taken down."), Contents, Now);

        var lifted = delisted.Apply(IndexStatusChange.Lift(delisted.Entries[0], "The takedown did not hold."), Contents, Now);

        Assert.Equal(IndexStatusFixtures.Current, lifted.Text);
        Assert.Empty(lifted.Entries);
    }

    [Theory]
    [InlineData("disputed", null, "delisted", null)]
    [InlineData("delisted", null, "disputed", null)]
    [InlineData("retracted", "1.0.0", "retracted", "1.0.0")]
    public void Apply_Duplicate_IsRefused(string existingState, string? existingVersion, string state, string? version)
    {
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current).Apply(Change(existingState, "my-pack", existingVersion), Contents, Now);

        Assert.Equal(IndexStatusRefusal.Duplicate, Refusal(document, Change(state, "my-pack", version)));
    }

    [Fact]
    public void Apply_OtherVersionOrAWholeStateNextToARetractedVersion_IsNoDuplicate()
    {
        var retracted = IndexStatusDocument.Parse(IndexStatusFixtures.Current).Apply(IndexStatusChange.Retract("my-pack", "1.0.0", "Broken."), Contents, Now);

        var both = retracted.Apply(IndexStatusChange.Retract("my-pack", "1.1.0", "Broken too."), Contents, Now);
        var delisted = both.Apply(IndexStatusChange.Delist("my-pack", "Taken down."), Contents, Now);

        Assert.Equal(["1.0.0", "1.1.0", null], delisted.Entries.Select(entry => entry.Version));
    }

    [Theory]
    [InlineData("delisted", "Missing", null, IndexStatusRefusal.UnknownId)]
    [InlineData("disputed", "README", null, IndexStatusRefusal.UnknownId)]
    [InlineData("retracted", "MyMod", "1.0.0", IndexStatusRefusal.NotAPack)]
    [InlineData("retracted", "Missing", "1.0.0", IndexStatusRefusal.NotAPack)]
    [InlineData("retracted", "my-pack", "2.0.0", IndexStatusRefusal.UnknownVersion)]
    [InlineData("retracted", "my-pack", null, IndexStatusRefusal.MissingVersion)]
    [InlineData("retracted", "my-pack", " ", IndexStatusRefusal.MissingVersion)]
    public void Apply_WhatTheStatusCheckRefuses_IsRefused(string state, string id, string? version, IndexStatusRefusal refusal)
    {
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        Assert.Equal(refusal, Refusal(document, Change(state, id, version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("First line.\nSecond line.")]
    [InlineData("First line.\u2028Second line.")]
    public void Apply_ReasonThatIsNoOneLine_IsRefused(string reason)
    {
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        Assert.Equal(IndexStatusRefusal.InvalidReason, Refusal(document, IndexStatusChange.Delist("MyMod", reason)));
    }

    [Theory]
    [InlineData("The author asked for it.", true)]
    [InlineData("  The author asked for it.  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("A pasted\treason.", false)]
    [InlineData("First line.\r\nSecond line.", false)]
    [InlineData("First line.\u2029Second line.", false)]
    public void Change_IsValidReason_TakesOneLineWithoutControlCharacters(string? reason, bool valid)
    {
        Assert.Equal(valid, IndexStatusChange.IsValidReason(reason));
    }

    [Theory]
    [InlineData("delisted", "MyMod", null, null)]
    [InlineData("retracted", "my-pack", "1.0.0", null)]
    [InlineData("delisted", "Missing", null, IndexStatusRefusal.UnknownId)]
    [InlineData("retracted", "MyMod", "1.0.0", IndexStatusRefusal.NotAPack)]
    [InlineData("retracted", "my-pack", "2.0.0", IndexStatusRefusal.UnknownVersion)]
    [InlineData("retracted", "my-pack", null, IndexStatusRefusal.MissingVersion)]
    public void Check_WithTheReasonNotTypedYet_RefusesOnlyWhatTheFileAndTheDocumentsRefuse(string state, string id, string? version, IndexStatusRefusal? refusal)
    {
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        var thrown = Record.Exception(() => document.Check(Change(state, id, version) with { Reason = string.Empty }, Contents));

        Assert.True(thrown is null or IndexStatusRefusedException, thrown?.ToString());
        Assert.Equal(refusal, (thrown as IndexStatusRefusedException)?.Refusal);
    }

    [Fact]
    public void Check_OfAnIdThatHasAState_IsADuplicateAndALiftOfItPasses()
    {
        var disputed = IndexStatusDocument.Parse(IndexStatusFixtures.Current).Apply(IndexStatusChange.Dispute("MyMod", "Claimed twice."), Contents, Now);

        var duplicate = Assert.Throws<IndexStatusRefusedException>(() => disputed.Check(IndexStatusChange.Delist("mymod", string.Empty), Contents));
        disputed.Check(IndexStatusChange.Lift(disputed.Entries.Single(), string.Empty), Contents);

        Assert.Equal(IndexStatusRefusal.Duplicate, duplicate.Refusal);
    }

    [Fact]
    public void Apply_RetractOfAVersionShownWithoutItsBuildMetadata_WritesTheSpellingOfItsFile()
    {
        var contents = IndexContents.FromPaths(["packs/tools-pack/1.2.0+build.5.toml", "packs/tools-pack/1.3.0.toml", "packs/twin/1.0.0+a.toml", "packs/twin/1.0.0+b.toml"]);
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        var retracted = document.Apply(IndexStatusChange.Retract("tools-pack", "1.2.0", "Broken."), contents, Now);
        var exact = document.Apply(IndexStatusChange.Retract("twin", "1.0.0+b", "Broken."), contents, Now);

        Assert.Equal("1.2.0+build.5", retracted.Entries.Single().Version);
        Assert.Equal("1.0.0+b", exact.Entries.Single().Version);
        Assert.Equal(IndexStatusRefusal.Duplicate, Assert.Throws<IndexStatusRefusedException>(() => retracted.Apply(IndexStatusChange.Retract("tools-pack", "1.2.0", "Again."), contents, Now)).Refusal);
        Assert.Equal(IndexStatusRefusal.UnknownVersion, Assert.Throws<IndexStatusRefusedException>(() => document.Apply(IndexStatusChange.Retract("twin", "1.0.0", "Which one."), contents, Now)).Refusal);
    }

    [Fact]
    public void Apply_LiftOfAStateTheFileNoLongerHas_IsRefused()
    {
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        Assert.Equal(IndexStatusRefusal.NotInFile, Refusal(document, IndexStatusChange.Lift(new IndexStatusEntry("MyMod", "delisted", null, null, null), "Resolved.")));
    }

    [Fact]
    public void Apply_IdThatDiffersOnlyInCase_ResolvesWritesTheDocumentSpellingAndCountsAsTheSameId()
    {
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        var disputed = document.Apply(IndexStatusChange.Dispute("mymod", "Claimed twice."), Contents, Now);
        var retracted = document.Apply(IndexStatusChange.Retract("MY-PACK", "1.1.0", "Broken."), Contents, Now);

        Assert.Equal("MyMod", disputed.Entries.Single().Id);
        Assert.Equal("my-pack", retracted.Entries.Single().Id);
        Assert.Equal(IndexStatusRefusal.Duplicate, Refusal(disputed, IndexStatusChange.Delist("MYMOD", "Taken down.")));
        Assert.Equal(IndexStatusRefusal.UnknownVersion, Refusal(document, IndexStatusChange.Retract("my-pack", "1.1.0-RC", "Broken.")));
        Assert.Equal(document.Text, disputed.Apply(IndexStatusChange.Lift(new IndexStatusEntry("MYMOD", "disputed", null, null, null), "Resolved."), Contents, Now).Text);
    }

    [Fact]
    public void Apply_VehicleAndSaveIds_ResolveFromTheirFoldersAndAreNoPacks()
    {
        var contents = IndexContents.FromPaths(["saves/ExampleSave.toml", "vehicles/ExampleRover.toml", "vehicles/nested/Deep.toml", "saves/README.md", "Vehicles/Upper.toml"]);
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        var delisted = document.Apply(IndexStatusChange.Delist("examplesave", "The author asked for it."), contents, Now);

        Assert.Equal("ExampleSave", delisted.Entries.Single().Id);
        Assert.Equal("ExampleRover", contents.ListingId("EXAMPLEROVER"));
        Assert.Equal("vehicles/ExampleRover.toml", contents.ListingPath("examplerover"));
        Assert.Equal("saves/ExampleSave.toml", contents.ListingPath("ExampleSave"));
        Assert.Equal("listings/MyMod.toml", Contents.ListingPath("mymod"));
        Assert.Null(contents.ListingId("Deep"));
        Assert.Null(contents.ListingId("README"));
        Assert.Null(contents.ListingId("Upper"));
        Assert.Null(contents.ListingPath("Missing"));
        Assert.Equal(IndexStatusRefusal.NotAPack, Assert.Throws<IndexStatusRefusedException>(() => document.Apply(IndexStatusChange.Retract("ExampleRover", "1.0.0", "Broken."), contents, Now)).Refusal);
    }

    [Fact]
    public void Apply_ReasonWithQuotesBackslashesAndOtherLetters_IsEscapedAndReadBack()
    {
        const string reason = "Der \"Autor\" bat darum, C:\\mods ist kaputt \u00e4\u00f6\u00fc.";
        var document = IndexStatusDocument.Parse(IndexStatusFixtures.Current);

        var changed = document.Apply(IndexStatusChange.Delist("MyMod", reason), Contents, Now);

        Assert.Contains("reason = \"Der \\\"Autor\\\" bat darum, C:\\\\mods ist kaputt \u00e4\u00f6\u00fc.\"", changed.Text, StringComparison.Ordinal);
        Assert.Equal(reason, IndexStatusDocument.Parse(changed.Text).Entries.Single().Reason);
    }

    [Fact]
    public void Parse_EntriesAStewardWroteByHand_ReadsEveryKeyAndKeepsTheirComments()
    {
        var text = """
            # header

            [[entries]]
            id = 'SomeMod'   # a literal string
            state = "delisted"
            since = "2026-08-10T00:00:00Z"
            reason = "Tab\tand \u00e9 and \U0001F600."

            # about the pack
            [[ entries ]]
            id = "SomePack"
            state = "retracted"
            version = "1.2.0"
            reason = "..."
            # trailing
            """.ReplaceLineEndings("\n");

        var document = IndexStatusDocument.Parse(text);
        var withoutPack = document.Apply(IndexStatusChange.Lift(document.Entries[1], "Fixed."), IndexContents.FromPaths([]), Now);

        Assert.Equal(
            [
                new IndexStatusEntry("SomeMod", "delisted", null, "2026-08-10T00:00:00Z", "Tab\tand \u00e9 and \U0001F600."),
                new IndexStatusEntry("SomePack", "retracted", "1.2.0", null, "..."),
            ],
            document.Entries);
        Assert.Equal(text[..text.IndexOf("\n\n# about", StringComparison.Ordinal)] + "\n# trailing", withoutPack.Text);
    }

    [Theory]
    [InlineData("entries = [{ id = \"A\", state = \"delisted\" }]\n")]
    [InlineData("entries = []\n[[entries]]\nid = \"A\"\nstate = \"delisted\"\n")]
    [InlineData("# only a comment\n")]
    [InlineData("[[entries]]\nid = \"A\"\nstate = \"delisted\"\nnote = \"x\"\n")]
    [InlineData("[[entries]]\nid = \"A\"\nstate = \"delisted\"\nreason = \"\"\"\nlong\n\"\"\"\n")]
    [InlineData("[[entries]]\nid = \"A\"\nstate = \"delisted\"\nid = \"B\"\n")]
    [InlineData("[[entries]]\nid = \"A\"\n")]
    [InlineData("[[entries]]\nid = \"A\" x\nstate = \"delisted\"\n")]
    [InlineData("[[entries]]\nid = \"A\\q\"\nstate = \"delisted\"\n")]
    [InlineData("[other]\nkey = \"x\"\nentries = []\n")]
    public void Parse_FormThatBoreaDoesNotEdit_Throws(string text)
    {
        Assert.Throws<FormatException>(() => IndexStatusDocument.Parse(text));
    }

    [Fact]
    public void Change_NamesThePullRequestItsBranchAndMentionsTheOwners()
    {
        var delist = IndexStatusChange.Delist("MyMod", "The author asked for it. ");
        var retract = IndexStatusChange.Retract("my-pack", "1.0.0", "Broken.");
        var lift = IndexStatusChange.Lift(new IndexStatusEntry("MyMod", "disputed", null, null, null), "Resolved.");

        Assert.Equal(("Delist MyMod", "steward/delisted-mymod"), (delist.Title, delist.Branch));
        Assert.Equal(("Retract my-pack 1.0.0", "steward/retracted-my-pack-1.0.0"), (retract.Title, retract.Branch));
        Assert.Equal(("Lift MyMod", "steward/lift-mymod"), (lift.Title, lift.Branch));
        Assert.Equal("Sets `delisted` on `MyMod` in `index-status.toml`.\n\nReason: The author asked for it.\n\n@alice owns `MyMod`.", delist.Body(["alice"]));
        Assert.Equal("Sets `retracted` on version 1.0.0 of `my-pack` in `index-status.toml`.\n\nReason: Broken.\n\n@alice @bob own `my-pack`.", retract.Body(["alice", "bob"]));
        Assert.Equal("Lifts `disputed` from `MyMod` in `index-status.toml`.\n\nReason: Resolved.", lift.Body([]));
    }

    private static IndexStatusChange Change(string state, string id, string? version) => state switch
    {
        "delisted" => IndexStatusChange.Delist(id, "A reason."),
        "disputed" => IndexStatusChange.Dispute(id, "A reason."),
        _ => IndexStatusChange.Retract(id, version, "A reason."),
    };

    private static IndexStatusRefusal Refusal(IndexStatusDocument document, IndexStatusChange change) =>
        Assert.Throws<IndexStatusRefusedException>(() => document.Apply(change, Contents, Now)).Refusal;
}
