using Borea.Core.Listings;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

public sealed class TomlListingFormatTests
{
    private readonly TomlListingFormat _format = new();

    [Fact]
    public void Write_NewDraft_UsesTheLayoutOfTheListings()
    {
        var draft = new ListingDraft
        {
            Id = "MyMod",
            Name = "My \"Mod\"",
            Authors = ["Maxi", "Renan"],
            Abstract = "Back\\slash and a tab\there.",
            Description = "# My Mod\n\nIt's here.\n",
            License = "MIT",
            Tags = ["gameplay"],
            Releases = new ListingReleases("owner/MyMod", 4253, "github"),
            Links = [new ListingLink("forums", "https://forums.ahwoo.com/threads/my-mod.1/"), new ListingLink("repository", "https://github.com/owner/MyMod")],
            GameMin = "2026.9.10.5438",
            Loader = new ListingLoader("StarMap", "0.4.6"),
            Dependencies = [new ListingDependency("ModMenu", "required", "1.0.0")],
            Icon = new ListingImageRecord("https://example.com/icon.png") { Sha256 = new string('a', 64), Width = 512, Height = 512, Size = 1000 },
            DescriptionImages = [new ListingImageRecord("https://example.com/shot.png") { Id = "shot", Sha256 = new string('b', 64), Width = 800, Height = 600, Size = 2000, Attribution = "Me" }],
        };

        var text = _format.Write(draft.ToDocument());

        Assert.Equal(
            """"
            spec_version = 1
            id = "MyMod"
            type = "mod"
            name = "My \"Mod\""
            authors = ["Maxi", "Renan"]
            abstract = "Back\\slash and a tab\there."
            description = """
            # My Mod

            It's here.
            """
            license = "MIT"
            tags = ["gameplay"]

            [releases]
            github = "owner/MyMod"
            spacedock = 4253
            authority = "github"

            [links]
            forums = "https://forums.ahwoo.com/threads/my-mod.1/"
            repository = "https://github.com/owner/MyMod"

            [compatibility]
            game_min = "2026.9.10.5438"

            [loader]
            id = "StarMap"
            min = "0.4.6"

            [[dependencies]]
            id = "ModMenu"
            kind = "required"
            min = "1.0.0"

            [images.icon]
            url = "https://example.com/icon.png"
            sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            width = 512
            height = 512
            size = 1000

            [[images.description]]
            id = "shot"
            url = "https://example.com/shot.png"
            sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
            width = 800
            height = 600
            size = 2000
            attribution = "Me"

            """".ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void Write_ListedLoader_KeepsInstallAndProvidesUnchanged()
    {
        var draft = ListingDraft.FromDocument(_format.Read(StarMap)) with { Abstract = "Changed." };

        var text = _format.Write(draft.ToDocument());

        var install = StarMap.IndexOf("[install]", StringComparison.Ordinal);
        Assert.EndsWith(StarMap[install..], text, StringComparison.Ordinal);
        Assert.Contains("abstract = \"Changed.\"\n", text, StringComparison.Ordinal);
        var reread = _format.Read(text);
        Assert.Equal(Flatten(_format.Read(StarMap).GetTable("provides")!), Flatten(reread.GetTable("provides")!));
        Assert.Equal(Flatten(_format.Read(StarMap).GetTable("install")!), Flatten(reread.GetTable("install")!));
    }

    [Fact]
    public void Write_ListingInItsOwnLayout_GivesTheSameText()
    {
        Assert.Equal(Listed, _format.Write(_format.Read(Listed), Listed));
        Assert.Equal(StarMap, _format.Write(_format.Read(StarMap), StarMap));
    }

    [Theory]
    [InlineData("Uses ''' quotes.\n")]
    [InlineData("Ends on a quote'")]
    [InlineData("Control \u0001 character.\n")]
    [InlineData("Quotes \"\"\" and \\ and \"")]
    [InlineData("\nStarts with a newline.")]
    [InlineData("One line without a newline")]
    public void Write_AnyDescription_ReadsBackTheSame(string description)
    {
        var document = new ListingDraft { Id = "MyMod", Description = description }.ToDocument();

        var reread = _format.Read(_format.Write(document));

        Assert.Equal(description, reread.GetString("description"));
    }

    [Fact]
    public void Write_UnknownKeysAndQuotedKeys_ComeAfterTheKnownOnes()
    {
        var document = _format.Read("x_new = true\nid = \"A\"\n\"odd key\" = 1.5\n[links]\nwiki = \"https://w\"\nforums = \"https://f\"\n");

        var text = _format.Write(document);

        Assert.Equal("id = \"A\"\nx_new = true\n\"odd key\" = 1.5\n\n[links]\nforums = \"https://f\"\nwiki = \"https://w\"\n", text);
    }

    [Fact]
    public void Read_InvalidToml_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _format.Read("id = \n"));
    }

    private static string Flatten(AuthoredTable table) =>
        string.Join(";", table.Entries.Select(entry => $"{entry.Key}={(entry.Value is AuthoredTable child ? "{" + Flatten(child) + "}" : entry.Value is IReadOnlyList<object> list ? string.Join(",", list) : entry.Value)}"));

    internal static readonly string StarMap = """"
        spec_version = 1
        id = "StarMap"
        type = "mod-loader"
        name = "StarMap"
        authors = ["KlaasWhite"]
        abstract = "Mod loader that runs code mods for Kitten Space Agency."
        description = """
        A prototype arbitrary code modloader for Kitten Space Agency.
        """
        license = "MIT"
        tags = ["library"]

        [releases]
        github = "StarMapLoader/StarMap"

        [links]
        forums = "https://forums.ahwoo.com/threads/starmap-mod-loader.384/"
        repository = "https://github.com/StarMapLoader/StarMap"
        bugtracker = "https://github.com/StarMapLoader/StarMap/issues"

        [compatibility]
        game_min = "2026.8.3.5117"

        [install]
        target = "standalone"
        uninstall = [
          "Delete the StarMap directory. The game runs unmodded again with no further cleanup.",
        ]

        [provides]
        launch = "StarMap.exe"
        content-dir = "mods"

        [provides.configure]
        file = "StarMapConfig.json"
        format = "json"
        game-path = "GameLocation"

        [provides.instance]
        flag = "-InstancePath"
        variable = "STARMAP_INSTANCE_PATH"

        [provides.platform.linux]
        runtime = "dotnet"
        launch = "StarMap.dll"

        [provides.platform.macos]
        runtime = "dotnet"
        launch = "StarMap.dll"

        """".ReplaceLineEndings("\n");

    internal static readonly string Listed = """
        spec_version = 1
        id = "DeltaVMap"
        type = "mod"
        name = "DeltaVMap"
        authors = ["Maxi"]
        abstract = "An interactive, auto-generated delta-v subway map for Kitten Space Agency."
        description = '''
        An interactive delta-v map.

        ![The map](ksa-image:delta-v-map)
        '''
        license = "MIT"
        tags = ["user-interface"]

        [releases]
        github = "Maximilian-Nesslauer/KSA-DeltaVMap"
        spacedock = 4294
        authority = "github"

        [links]
        forums = "https://forums.ahwoo.com/threads/deltavmap.978/"
        repository = "https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap"
        spacedock = "https://spacedock.info/mod/4294/DeltaVMap"
        bugtracker = "https://github.com/Maximilian-Nesslauer/KSA-DeltaVMap/issues"

        [compatibility]
        game_min = "2026.9.10.5438"

        [loader]
        id = "StarMap"
        min = "0.4.5"

        [images.icon]
        url = "https://raw.githubusercontent.com/Maximilian-Nesslauer/KSA-DeltaVMap/88b39f875e9f1e35f7921ab545aae28ab89d2656/images/listing/icon.png"
        sha256 = "ed946e22305dbe7be6a14a8f2d501d597002c572162332db07d58dc4918ea41f"
        width = 512
        height = 512
        size = 42884

        [[images.description]]
        id = "delta-v-map"
        url = "https://raw.githubusercontent.com/Maximilian-Nesslauer/KSA-DeltaVMap/88b39f875e9f1e35f7921ab545aae28ab89d2656/images/listing/delta-v-map.png"
        sha256 = "761cfe0f3fc923845c15884b28da8c3f03fb1922e44f2295ad06066e0600c4ec"
        width = 2048
        height = 1191
        size = 497028

        """.ReplaceLineEndings("\n");
}
