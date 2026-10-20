using Borea.Core.Listings;

namespace Borea.Core.Tests.Listings;

public sealed class ListingDraftTests
{
    [Fact]
    public void ToDocument_NewDraft_WritesOnlyWhatItHas()
    {
        var draft = new ListingDraft
        {
            Id = "MyMod",
            Name = "My Mod",
            Authors = ["Maxi"],
            Abstract = "Does a thing.",
            License = "MIT",
            Links = [new ListingLink("forums", "https://forums.ahwoo.com/threads/my-mod.1/")],
            GameMin = "2026.9",
        };

        var document = draft.ToDocument();

        Assert.Equal(["spec_version", "id", "type", "name", "authors", "abstract", "license", "links", "compatibility"], document.Entries.Select(entry => entry.Key));
        Assert.Equal(1L, document["spec_version"]);
        Assert.Equal("mod", document["type"]);
        Assert.Equal("2026.9", document.GetTable("compatibility")!.GetString("game_min"));
        Assert.Equal("listings/MyMod.toml", draft.Path);
        Assert.False(draft.IsEdit);
    }

    [Theory]
    [InlineData("vehicle", "vehicles/ExampleRover.toml")]
    [InlineData("save", "saves/ExampleRover.toml")]
    public void ToDocument_VehicleOrSaveWithoutForumsAndGameMin_WritesNeitherTableIntoItsFolder(string type, string path)
    {
        var draft = new ListingDraft
        {
            Id = "ExampleRover",
            Type = type,
            Name = "Example Rover",
            Authors = ["Maxi"],
            Abstract = "A rover.",
            License = "CC-BY-4.0",
        };

        var document = draft.ToDocument();

        Assert.Equal(["spec_version", "id", "type", "name", "authors", "abstract", "license"], document.Entries.Select(entry => entry.Key));
        Assert.Equal(path, draft.Path);
        Assert.True(draft.IsVehicleOrSave);
    }

    [Fact]
    public void ToDocument_VehicleWithGameMaxOnly_KeepsCompatibilityWithoutGameMin()
    {
        var draft = new ListingDraft { Id = "ExampleRover", Type = "vehicle", GameMax = "2026.10.13.5602" };

        var compatibility = draft.ToDocument().GetTable("compatibility")!;

        Assert.Equal(["game_max"], compatibility.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void ToDocument_ModWithoutForumsAndGameMin_StillWritesBothTables()
    {
        var document = new ListingDraft { Id = "MyMod" }.ToDocument();

        Assert.NotNull(document.GetTable("links"));
        Assert.Equal(string.Empty, document.GetTable("compatibility")!.GetString("game_min"));
    }

    [Fact]
    public void FromDocument_ThenToDocument_KeepsWhatThePageDoesNotEdit()
    {
        var document = Listed();

        var draft = ListingDraft.FromDocument(document);
        var written = (draft with { Name = "Renamed" }).ToDocument();

        Assert.True(draft.IsEdit);
        Assert.False(draft.IsNextVersion);
        Assert.Equal("mod-loader", written["type"]);
        Assert.Equal("Renamed", written["name"]);
        Assert.Equal("standalone", written.GetTable("install")!.GetString("target"));
        Assert.Equal("StarMap.exe", written.GetTable("provides")!.GetString("launch"));
        Assert.Equal(new object[] { "windows" }, written.GetTable("compatibility")!.GetList("os")!);
        Assert.Equal("https://example.com/wiki", written.GetTable("links")!.GetString("wiki"));
        Assert.Equal("kept", written["x_future"]);
    }

    [Fact]
    public void FromDocument_Since_IsReadAndWrittenBackWithTheOtherReleasesKeys()
    {
        var document = Listed();
        document.GetTable("releases")!.Set("since", "1.2");
        document.GetTable("releases")!.Set("x_future", "kept");

        var draft = ListingDraft.FromDocument(document);
        var written = (draft with { Releases = draft.Releases! with { Since = "0.9" } }).ToDocument().GetTable("releases")!;
        var cleared = (draft with { Releases = draft.Releases! with { Since = null } }).ToDocument().GetTable("releases")!;

        Assert.Equal(new ListingReleases("StarMapLoader/StarMap", null, null, "1.2"), draft.Releases);
        Assert.Equal(["github", "since", "x_future"], written.Entries.Select(entry => entry.Key));
        Assert.Equal("0.9", written.GetString("since"));
        Assert.Equal("kept", written.GetString("x_future"));
        Assert.False(cleared.Contains("since"));
    }

    [Fact]
    public void FromDocument_TagPrefix_IsReadAndWrittenBackWithTheOtherReleasesKeys()
    {
        var document = Listed();
        document.GetTable("releases")!.Set("tag_prefix", "starmap-v");
        document.GetTable("releases")!.Set("x_future", "kept");

        var draft = ListingDraft.FromDocument(document);
        var unchanged = draft.ToDocument().GetTable("releases")!;
        var written = (draft with { Releases = draft.Releases! with { TagPrefix = "release/" } }).ToDocument().GetTable("releases")!;
        var cleared = (draft with { Releases = draft.Releases! with { TagPrefix = null } }).ToDocument().GetTable("releases")!;

        Assert.Equal(new ListingReleases("StarMapLoader/StarMap", null, TagPrefix: "starmap-v"), draft.Releases);
        Assert.Equal("starmap-v", unchanged.GetString("tag_prefix"));
        Assert.Equal(["github", "tag_prefix", "x_future"], written.Entries.Select(entry => entry.Key));
        Assert.Equal("release/", written.GetString("tag_prefix"));
        Assert.Equal("kept", written.GetString("x_future"));
        Assert.False(cleared.Contains("tag_prefix"));
    }

    [Fact]
    public void FromDocument_ShareHostAlone_IsReadAndWrittenBack()
    {
        var document = Listed();
        document.GetTable("releases")!.Remove("github");
        document.GetTable("releases")!.Set("ksamods", "ExampleRover");

        var draft = ListingDraft.FromDocument(document);

        Assert.Equal(new ListingReleases(null, null, Share: new ListingShareHost("ksamods", "ExampleRover")), draft.Releases);
        Assert.Equal("ExampleRover", draft.ToDocument().GetTable("releases")!.GetString("ksamods"));
    }

    [Fact]
    public void FromDocument_ShareHostNamedByTheAuthority_KeepsEveryKey()
    {
        var document = Listed();
        document.GetTable("releases")!.Set("ksamods", "ExampleRover");
        document.GetTable("releases")!.Set("authority", "ksamods");

        var written = ListingDraft.FromDocument(document).ToDocument().GetTable("releases")!;

        Assert.Equal(["github", "ksamods", "authority"], written.Entries.Select(entry => entry.Key));
        Assert.Equal(("StarMapLoader/StarMap", "ExampleRover", "ksamods"), (written.GetString("github"), written.GetString("ksamods"), written.GetString("authority")));
    }

    [Fact]
    public void FromDocument_DependencyWithAlternatives_IsWrittenBackUnchanged()
    {
        var alternatives = new AuthoredTable();
        alternatives.Set("kind", "required");
        var first = new AuthoredTable();
        first.Set("id", "A");
        var second = new AuthoredTable();
        second.Set("id", "B");
        alternatives.Set("any_of", new List<object> { first, second });
        var plain = new AuthoredTable();
        plain.Set("id", "C");
        plain.Set("kind", "optional");
        var document = Listed();
        document.Set("dependencies", new List<object> { alternatives, plain });

        var draft = ListingDraft.FromDocument(document);
        var written = draft.ToDocument().GetList("dependencies")!.Cast<AuthoredTable>().ToList();

        Assert.NotNull(draft.Dependencies[0].Preserved);
        Assert.Null(draft.Dependencies[1].Preserved);
        Assert.Equal(["A", "B"], written[0].GetList("any_of")!.Cast<AuthoredTable>().Select(item => item.GetString("id")));
        Assert.Equal("optional", written[1].GetString("kind"));
    }

    [Fact]
    public void ToDocument_EmptyFields_RemoveTheirKeys()
    {
        var draft = ListingDraft.FromDocument(Listed()) with
        {
            Tags = [],
            Description = null,
            Releases = null,
            Icon = null,
            DescriptionImages = [],
        };

        var document = draft.ToDocument();

        Assert.False(document.Contains("tags"));
        Assert.False(document.Contains("description"));
        Assert.False(document.Contains("releases"));
        Assert.False(document.Contains("images"));
    }

    [Fact]
    public void ToDocument_Images_WriteTheirRecords()
    {
        var draft = new ListingDraft
        {
            Id = "MyMod",
            Icon = new ListingImageRecord("https://example.com/icon.png") { Sha256 = new string('a', 64), Width = 512, Height = 512, Size = 1000 },
            DescriptionImages = [new ListingImageRecord("https://example.com/shot.png") { Id = "shot", License = "CC0-1.0" }],
        };

        var images = draft.ToDocument().GetTable("images")!;

        Assert.Equal(512L, images.GetTable("icon")!["width"]);
        Assert.False(images.GetTable("icon")!.Contains("id"));
        var shot = (AuthoredTable)images.GetList("description")![0];
        Assert.Equal("shot", shot.GetString("id"));
        Assert.Equal("CC0-1.0", shot.GetString("license"));
        Assert.False(shot.Contains("sha256"));
    }

    [Fact]
    public void ToDocument_PackDraft_WritesThePackKeysForItsOwnVersionFile()
    {
        var draft = new ListingDraft
        {
            Id = "my-pack",
            Type = ListingDraft.ModPackType,
            Name = "My Pack",
            Version = "1.0.1",
            ReleasedAt = "2026-09-25T12:00:00Z",
            Mods = [new ListingPackMember("DeltaVMap", "1.2.6"), new ListingPackMember("Compendium", "0.9.13")],
        };

        var document = draft.ToDocument();

        Assert.Equal("packs/my-pack/1.0.1.toml", draft.Path);
        Assert.Equal("modpack", document["type"]);
        Assert.Equal("1.0.1", document["version"]);
        Assert.Equal("2026-09-25T12:00:00Z", document["released_at"]);
        Assert.False(document.Contains("changelog"));
        Assert.Equal(
            [("DeltaVMap", "1.2.6"), ("Compendium", "0.9.13")],
            document.GetList("mods")!.Cast<AuthoredTable>().Select(entry => (entry.GetString("id"), entry.GetString("version"))));
    }

    [Fact]
    public void FromDocument_Pack_ThenToDocument_KeepsTheKeysItDoesNotName()
    {
        var document = new AuthoredTable();
        document.Set("spec_version", 1L);
        document.Set("id", "my-pack");
        document.Set("type", "modpack");
        document.Set("version", "1.0.0");
        document.Set("released_at", "2026-09-23T20:00:00Z");
        document.Set("changelog", "First version.");
        document.Set("x_future", "kept");
        var member = new AuthoredTable();
        member.Set("id", "DeltaVMap");
        member.Set("version", "1.2.6");
        member.Set("x_note", "kept too");
        document.Set("mods", new List<object> { member });
        var vehicle = new AuthoredTable();
        vehicle.Set("id", "Rocket");
        vehicle.Set("version", "1.0.0");
        document.Set("vehicles", new List<object> { vehicle });

        var draft = ListingDraft.FromDocument(document);
        var written = (draft with { Version = "1.0.1", Mods = [draft.Mods[0] with { Version = "1.2.7" }] }).ToDocument();

        Assert.True(draft.IsPack);
        Assert.False(draft.IsEdit);
        Assert.True(draft.IsNextVersion);
        Assert.Equal("First version.", draft.Changelog);
        Assert.Equal("1.0.1", written["version"]);
        Assert.Equal("kept", written["x_future"]);
        Assert.Equal("Rocket", ((AuthoredTable)written.GetList("vehicles")![0]).GetString("id"));
        var pin = (AuthoredTable)written.GetList("mods")![0];
        Assert.Equal("1.2.7", pin.GetString("version"));
        Assert.Equal("kept too", pin.GetString("x_note"));
    }

    [Fact]
    public void Set_ValueThatNoDocumentHolds_Throws()
    {
        var table = new AuthoredTable();

        Assert.Throws<ArgumentException>(() => table.Set("when", DateTimeOffset.UnixEpoch));
    }

    private static AuthoredTable Listed()
    {
        var document = new AuthoredTable();
        document.Set("spec_version", 1L);
        document.Set("id", "StarMap");
        document.Set("type", "mod-loader");
        document.Set("name", "StarMap");
        document.Set("authors", new List<object> { "KlaasWhite" });
        document.Set("abstract", "Loader.");
        document.Set("description", "Text.\n");
        document.Set("license", "MIT");
        document.Set("tags", new List<object> { "library" });
        document.Set("x_future", "kept");
        var releases = new AuthoredTable();
        releases.Set("github", "StarMapLoader/StarMap");
        document.Set("releases", releases);
        var links = new AuthoredTable();
        links.Set("forums", "https://forums.ahwoo.com/threads/starmap-mod-loader.384/");
        links.Set("wiki", "https://example.com/wiki");
        document.Set("links", links);
        var compatibility = new AuthoredTable();
        compatibility.Set("game_min", "2026.8.3.5117");
        compatibility.Set("os", new List<object> { "windows" });
        document.Set("compatibility", compatibility);
        var install = new AuthoredTable();
        install.Set("target", "standalone");
        document.Set("install", install);
        var provides = new AuthoredTable();
        provides.Set("launch", "StarMap.exe");
        document.Set("provides", provides);
        return document;
    }
}
