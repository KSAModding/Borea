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

    [Fact]
    public void FromDocument_ThenToDocument_KeepsWhatThePageDoesNotEdit()
    {
        var document = Listed();

        var draft = ListingDraft.FromDocument(document);
        var written = (draft with { Name = "Renamed" }).ToDocument();

        Assert.True(draft.IsEdit);
        Assert.Equal("mod-loader", written["type"]);
        Assert.Equal("Renamed", written["name"]);
        Assert.Equal("standalone", written.GetTable("install")!.GetString("target"));
        Assert.Equal("StarMap.exe", written.GetTable("provides")!.GetString("launch"));
        Assert.Equal(new object[] { "windows" }, written.GetTable("compatibility")!.GetList("os")!);
        Assert.Equal("https://example.com/wiki", written.GetTable("links")!.GetString("wiki"));
        Assert.Equal("kept", written["x_future"]);
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
