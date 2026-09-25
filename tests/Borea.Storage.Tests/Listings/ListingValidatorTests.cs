using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Tags;
using Borea.Storage.Listings;

namespace Borea.Storage.Tests.Listings;

public sealed class ListingValidatorTests
{
    private readonly ListingValidator _validator = new(new EmbeddedSchema());
    private readonly TomlListingFormat _format = new();

    [Fact]
    public void EmbeddedSchema_LoadsAsDraft202012()
    {
        Assert.True(ListingSchemaStore.Loads(ListingSchemaStore.EmbeddedText));
        Assert.Equal(ListingSchemaOrigin.Embedded, _validator.SchemaOrigin);
    }

    [Fact]
    public void Validate_ListingsInTheLayoutOfContentIndex_PassWithoutErrors()
    {
        foreach (var text in new[] { TomlListingFormatTests.StarMap, TomlListingFormatTests.Listed })
        {
            var result = _validator.Validate(_format.Read(text), new ListingCheckContext(Snapshot(), ListedId: _format.Read(text).GetString("id")));

            Assert.False(result.HasErrors, string.Join("\n", result.Issues));
        }
    }

    [Fact]
    public void Validate_WrittenDraft_RoundTripsAndPasses()
    {
        var text = _format.Write(Valid().ToDocument());

        var result = _validator.Validate(_format.Read(text), new ListingCheckContext(Snapshot()));

        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("id", "CON", "id", "'CON' is a reserved name, compared up to the first dot")]
    [InlineData("id", "-bad", "id", "'-bad' does not have the right form")]
    [InlineData("forums", "https://example.com/thread", "links.forums", "'https://example.com/thread' is not an https link to a thread on forums.ahwoo.com")]
    [InlineData("license", "MIT, GPL", "license", "'MIT, GPL' is not an SPDX license expression")]
    [InlineData("game_min", "2026.13", "compatibility.game_min", "'2026.13' does not have the right form")]
    [InlineData("name", "", "name", "cannot be empty")]
    [InlineData("github", "not a repo", "releases.github", "'not a repo' does not have the right form; for example, use 'owner/repository'")]
    public void Validate_SchemaRejection_SaysItInWords(string field, string value, string location, string message)
    {
        var draft = field switch
        {
            "id" => Valid() with { Id = value },
            "forums" => Valid() with { Links = [new ListingLink("forums", value)] },
            "license" => Valid() with { License = value },
            "game_min" => Valid() with { GameMin = value },
            "name" => Valid() with { Name = value },
            _ => Valid() with { Releases = new ListingReleases(value, null) },
        };

        var errors = Errors(draft);

        Assert.Contains(errors, issue => issue.Location == location && issue.Message.StartsWith(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingAndForbiddenKeys_AreNamed()
    {
        var document = Valid().ToDocument();
        document.Remove("abstract");
        document.Set("extra", "x");
        var provides = new AuthoredTable();
        provides.Set("launch", "x.exe");
        document.Set("provides", provides);
        var releases = new AuthoredTable();
        releases.Set("github", "a/b");
        releases.Set("spacedock", 4L);
        document.Set("releases", releases);

        var errors = _validator.Validate(document, new ListingCheckContext(null)).Issues;

        Assert.Contains(new ListingIssue(ListingIssueSeverity.Error, string.Empty, "'abstract' is a required property"), errors);
        Assert.Contains(new ListingIssue(ListingIssueSeverity.Error, string.Empty, "'extra' is not a known key here"), errors);
        Assert.Contains(new ListingIssue(ListingIssueSeverity.Error, "provides", "this key is not allowed here"), errors);
        Assert.Contains(new ListingIssue(ListingIssueSeverity.Error, "releases", "'authority' is a required property"), errors);
    }

    [Fact]
    public void Validate_ReleasesWithoutAHost_NeedsOne()
    {
        var document = Valid().ToDocument();
        document.Set("releases", new AuthoredTable());

        var errors = _validator.Validate(document, new ListingCheckContext(null)).Issues;

        Assert.Contains(new ListingIssue(ListingIssueSeverity.Error, "releases", "needs one of 'github' or 'spacedock'"), errors);
    }

    [Theory]
    [InlineData("MIT OR Apache-2.0", null)]
    [InlineData("GPL-2.0-only WITH Classpath-exception-2.0", null)]
    [InlineData("LicenseRef-MyModLicense", null)]
    [InlineData("gpl-2.0+", null)]
    [InlineData("Foo-1.0", "'Foo-1.0' names Foo-1.0, which is not on the SPDX license list; the identifiers are at https://spdx.org/licenses/")]
    [InlineData("MIT AND", "'MIT AND' does not parse as an SPDX license expression; join several licenses with AND or OR, such as GPL-2.0-only AND CC-BY-SA-4.0")]
    [InlineData("(MIT OR GPL-3.0-only", "'(MIT OR GPL-3.0-only' has unbalanced parentheses")]
    [InlineData("MIT WITH GPL-3.0-only", "'MIT WITH GPL-3.0-only' names GPL-3.0-only, which is not on the SPDX license list; the identifiers are at https://spdx.org/licenses/")]
    public void Validate_LicenseExpression_ResolvesAgainstTheSpdxList(string license, string? message)
    {
        var errors = Errors(Valid() with { License = license }).Where(issue => issue.Location == "license").ToList();

        if (message is null)
            Assert.Empty(errors);
        else
            Assert.Contains(errors, issue => issue.Message == message);
    }

    [Fact]
    public void Validate_IdOfAnotherListing_CollidesCaseInsensitively()
    {
        Assert.Contains(Errors(Valid() with { Id = "starmap" }), issue => issue.Location == "id" && issue.Message.Contains("already held by listings/StarMap.toml", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_OwnIdInAnEdit_IsNoCollision()
    {
        var draft = Valid() with { Id = "AdvancedFlightComputer" };

        var result = _validator.Validate(draft.ToDocument(), new ListingCheckContext(Snapshot(), ListedId: "AdvancedFlightComputer"));

        Assert.DoesNotContain(result.Issues, issue => issue.Location == "id");
    }

    [Fact]
    public void Validate_References_NeedTheRightTypeAndSpelling()
    {
        var draft = Valid() with
        {
            Loader = new ListingLoader("AdvancedFlightComputer", "1.0.0"),
            Dependencies = [new ListingDependency("starmap", "required")],
        };

        var errors = Errors(draft);

        Assert.Contains(errors, issue => issue.Location == "loader" && issue.Message == "'AdvancedFlightComputer' is listed as a mod, and a loader has to be a mod-loader");
        Assert.Contains(errors, issue => issue.Location == "dependencies[0]" && issue.Message == "'starmap' does not use the canonical id spelling 'StarMap'");
        Assert.Contains(errors, issue => issue.Location == "dependencies[0]" && issue.Message == "'starmap' is listed as a mod-loader, and a dependency has to be a mod");
    }

    [Fact]
    public void Validate_BoundsTheWrongWayRound_AreErrors()
    {
        var draft = Valid() with
        {
            GameMin = "2026.9.10.5438",
            GameMax = "2026.8.19.5261",
            Loader = new ListingLoader("StarMap", "0.5.0", "0.4.6"),
        };

        var errors = Errors(draft);

        Assert.Contains(errors, issue => issue.Location == "compatibility" && issue.Message == "game_max '2026.8.19.5261' is older than game_min '2026.9.10.5438'");
        Assert.Contains(errors, issue => issue.Location == "loader" && issue.Message == "max '0.4.6' is below min '0.5.0'");
    }

    [Fact]
    public void Validate_ShortLoaderBound_PassesAndComparesFilled()
    {
        Assert.Empty(Errors(Valid() with { Loader = new ListingLoader("StarMap", "0.5", "0.5.0") }));

        var errors = Errors(Valid() with { Loader = new ListingLoader("StarMap", "0.6", "0.5.9") });

        Assert.Contains(errors, issue => issue.Location == "loader" && issue.Message == "max '0.5.9' is below min '0.6'");
    }

    [Theory]
    [InlineData("0.5.0.1")]
    [InlineData("01.5")]
    public void Validate_LoaderBoundThatIsNoVersion_IsRefused(string min)
    {
        var errors = Errors(Valid() with { Loader = new ListingLoader("StarMap", min) });

        Assert.Contains(errors, issue => issue.Location == "loader.min" && issue.Message.StartsWith($"'{min}' is not a version", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2026.99999999999", "2026.99999999998")]
    [InlineData("2026.1.1.123456789012345678901", "2026.1.1.123456789012345678900")]
    public void Validate_BoundsBeyondLong_CompareWithoutThrowing(string min, string max)
    {
        var errors = Errors(Valid() with { GameMin = min, GameMax = max });

        Assert.Contains(errors, issue => issue.Location == "compatibility" && issue.Message == $"game_max '{max}' is older than game_min '{min}'");
    }

    [Fact]
    public void Validate_MonthWithoutABuild_IsAnError()
    {
        Assert.Contains(Errors(Valid() with { GameMin = "2026.3" }), issue => issue.Location == "compatibility.game_min" && issue.Message == "'2026.3' names a month with no build in the game release list");
        Assert.DoesNotContain(Errors(Valid() with { GameMin = "2026.9" }), issue => issue.Location == "compatibility.game_min");
    }

    [Fact]
    public void Validate_DependencyOnItselfOrTwice_IsAnError()
    {
        var draft = Valid() with { Dependencies = [new ListingDependency("MyMod", "required"), new ListingDependency("Other", "optional"), new ListingDependency("other", "conflict")] };

        var errors = Errors(draft);

        Assert.Contains(errors, issue => issue.Location == "dependencies[0]" && issue.Message == "a listing cannot depend on itself");
        Assert.Contains(errors, issue => issue.Location == "dependencies[2]" && issue.Message == "'other' already has a dependency entry");
    }

    [Fact]
    public void Validate_EmptyDependencyIdWithoutAListingId_IsNotADependencyOnItself()
    {
        var draft = Valid() with { Id = "", Dependencies = [new ListingDependency("", "required")] };

        Assert.DoesNotContain(Errors(draft), issue => issue.Message == "a listing cannot depend on itself");
    }

    [Fact]
    public void Validate_Tags_GiveNotesOnly()
    {
        var result = Check(Valid() with { Tags = ["my-tag"] });

        Assert.False(result.HasErrors);
        Assert.Contains(result.Issues, issue => issue.Severity == ListingIssueSeverity.Note && issue.Message.StartsWith("'my-tag' is not a curated tag", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Severity == ListingIssueSeverity.Note && issue.Message.StartsWith("the document has no curated tag", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LongAbstractAndSharedForumsThread_GiveNotes()
    {
        var draft = Valid() with
        {
            Abstract = new string('a', 281),
            Links = [new ListingLink("forums", "https://forums.ahwoo.com/threads/advanced-flight-computer.783/")],
        };

        var notes = Check(draft).Issues.Where(issue => issue.Severity == ListingIssueSeverity.Note).ToList();

        Assert.Contains(notes, issue => issue.Location == "abstract" && issue.Message.StartsWith("281 characters is longer than 280", StringComparison.Ordinal));
        Assert.Contains(notes, issue => issue.Location == "links.forums" && issue.Message.StartsWith("thread 783 is also the forums thread of listings/AdvancedFlightComputer.toml", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AbstractLength_CountsCodePoints()
    {
        var draft = Valid() with { Abstract = string.Concat(Enumerable.Repeat("\U0001F680", 280)) };

        Assert.DoesNotContain(Check(draft).Issues, issue => issue.Location == "abstract");
    }

    [Fact]
    public void Validate_ImageRecords_FollowTheImageChecks()
    {
        var draft = Valid() with
        {
            Description = "![Map](ksa-image:map) ![Gone](ksa-image:gone) ![Web](https://example.com/a.png)",
            Icon = Image("https://example.com/icon.png", 1024, 512) with { License = "Bad License" },
            DescriptionImages = [Image("https://example.com/map.png", 800, 600) with { Id = "map" }, Image("https://example.com/b.png", 800, 600) with { Id = "map" }, Image("https://example.com/c.png", 800, 600) with { Id = "unused" }],
        };

        var result = Check(draft);

        Assert.Contains(result.Issues, issue => issue.Location == "images.icon" && issue.Severity == ListingIssueSeverity.Note && issue.Message == "the icon is 1024 by 512 pixels, so clients show the square from 256,0 to 768,512");
        Assert.Contains(result.Issues, issue => issue.Location == "images.icon.license" && issue.Severity == ListingIssueSeverity.Error);
        Assert.Contains(result.Issues, issue => issue.Location == "images.description[1]" && issue.Message == "id 'map' is already used by images.description[0]");
        Assert.Contains(result.Issues, issue => issue.Location == "description" && issue.Severity == ListingIssueSeverity.Error && issue.Message == "'ksa-image:gone' names no record in [[images.description]]");
        Assert.Contains(result.Issues, issue => issue.Location == "description" && issue.Severity == ListingIssueSeverity.Note && issue.Message.StartsWith("the image 'https://example.com/a.png' is not a ksa-image: reference", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Location == "images.description[2]" && issue.Message == "nothing in the description references 'unused', so no client shows it");
    }

    [Fact]
    public void Validate_IconOutsideTheSchemaLimits_IsASchemaError()
    {
        var errors = Errors(Valid() with { Icon = Image("https://example.com/icon.png", 200, 200) with { Sha256 = "short" } });

        Assert.Contains(errors, issue => issue.Location == "images.icon.width" && issue.Message == "200 is less than the minimum of 256");
        Assert.Contains(errors, issue => issue.Location == "images.icon.sha256" && issue.Message == "'short' is not a hex SHA-256 digest of 64 characters");
    }

    [Theory]
    [InlineData("MyMod", null)]
    [InlineData("mymod", "the archive's top-level directory is 'MyMod' and the id is 'mymod': the folder name is the identity the game sees, so the casing has to match")]
    [InlineData("Other", "the archive's top-level directory is 'MyMod', which does not match the id 'Other'")]
    public void Validate_ArchiveRoot_MustMatchTheId(string id, string? message)
    {
        var archive = new ListingArchiveFacts("MyMod", ["MyMod"], ModToml: true, EntryAssembly: "MyMod", IsCodeMod: true);

        var errors = _validator.Validate((Valid() with { Id = id }).ToDocument(), new ListingCheckContext(null, Archive: archive)).Issues
            .Where(issue => issue.Location == "id").ToList();

        if (message is null)
            Assert.Empty(errors);
        else
            Assert.Contains(errors, issue => issue.Message == message);
    }

    [Fact]
    public void Validate_ArchiveWithoutRoot_NeedsOneFolder()
    {
        var archive = new ListingArchiveFacts(null, ["A", "B"], ModToml: false, EntryAssembly: null, IsCodeMod: false);

        var errors = _validator.Validate(Valid().ToDocument(), new ListingCheckContext(null, Archive: archive)).Issues;

        Assert.Contains(errors, issue => issue.Location == "id" && issue.Message.StartsWith("the install root is neither derivable from the archive nor authored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadSchemaAsync_UsesTheSchemaTheSourceGives()
    {
        var validator = new ListingValidator(new CachedSchema());

        Assert.Equal(ListingSchemaOrigin.Cached, await validator.LoadSchemaAsync());
        Assert.Equal(ListingSchemaOrigin.Cached, validator.SchemaOrigin);
        Assert.Empty(validator.Validate(Valid().ToDocument(), new ListingCheckContext(Snapshot())).Issues);
    }

    private List<ListingIssue> Errors(ListingDraft draft) =>
        Check(draft).Issues.Where(issue => issue.Severity == ListingIssueSeverity.Error).ToList();

    private ListingCheckResult Check(ListingDraft draft) => _validator.Validate(draft.ToDocument(), new ListingCheckContext(Snapshot()));

    private static ListingImageRecord Image(string url, long width, long height) =>
        new(url) { Sha256 = new string('a', 64), Width = width, Height = height, Size = 1000 };

    private static ListingDraft Valid() => new()
    {
        Id = "MyMod",
        Name = "My Mod",
        Authors = ["Maxi"],
        Abstract = "Does a thing.",
        License = "MIT",
        Tags = ["gameplay"],
        Releases = new ListingReleases("owner/MyMod", null),
        Links = [new ListingLink("forums", "https://forums.ahwoo.com/threads/my-mod.9/")],
        GameMin = "2026.9.10.5438",
        Loader = new ListingLoader("StarMap", "0.4.6"),
    };

    private static ContentIndexSnapshot Snapshot()
    {
        var links = new Dictionary<string, string> { ["forums"] = "https://forums.ahwoo.com/threads/advanced-flight-computer.783/" };
        var afc = new ModMetadata(1, "AdvancedFlightComputer", "index", "AFC", ["Maxi"], "Abstract.", "MIT", links, "2026.9.10.5438");
        var starMap = new ModMetadata(1, "StarMap", "index", "StarMap", ["KlaasWhite"], "Abstract.", "MIT",
            new Dictionary<string, string> { ["forums"] = "https://forums.ahwoo.com/threads/starmap-mod-loader.384/" }, "2026.8.3.5117", ContentType.ModLoader);
        var tags = new CuratedTagVocabulary(1, [new CuratedTag("gameplay", "Gameplay", "Mechanics.", "Gameplay"), new CuratedTag("library", "Library", "Code.")]);
        return new ContentIndexSnapshot(
            1,
            [new ContentIndexListing("AdvancedFlightComputer", afc, Array.Empty<ModVersionMetadata>(), null), new ContentIndexListing("StarMap", starMap, Array.Empty<ModVersionMetadata>(), null)],
            [],
            new ContentIndexGameVersions(1, "test", ["2026.8.19.5261", "2026.9.7.5402", "2026.9.10.5438"]),
            [],
            tags);
    }

    private sealed class EmbeddedSchema : IListingSchemaSource
    {
        public Task<ListingSchema> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListingSchema(ListingSchemaStore.EmbeddedText, ListingSchemaOrigin.Embedded));
    }

    private sealed class CachedSchema : IListingSchemaSource
    {
        public Task<ListingSchema> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListingSchema(ListingSchemaStore.EmbeddedText, ListingSchemaOrigin.Cached));
    }
}
