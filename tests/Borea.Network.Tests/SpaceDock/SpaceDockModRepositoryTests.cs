using Borea.Core.Mods;
using Borea.Network.SpaceDock;

namespace Borea.Network.Tests;

public sealed class SpaceDockModRepositoryTests
{
    // Trimmed/adapted from the real /api/mod/4165 response for "MPFX".
    private const string RealMpfxModJson = """
        {"name":"MPFX","id":4165,"game":"Kitten Space Agency","game_id":22409,
         "short_description":"Simple post processing","author":"AMPW","license":"MIT",
         "website":"https://forums.ahwoo.com/threads/mpfx-v0-1-0-simple-post-processing-effects.812/",
         "source_code":"https://github.com/AMPW-german/MPFX",
         "default_version_id":24490,"url":"/mod/4165/MPFX",
         "versions":[
           {"friendly_version":"v0.3.1","game_version":"2026.7.10.5056","id":24490,
            "created":"2026-07-23T20:02:32.835271+00:00",
            "download_path":"/mod/4165/MPFX/download/v0.3.1","changelog":"Built for KSA"},
           {"friendly_version":"v0.2.0","game_version":"2026.3.3.3759","id":23419,
            "created":"2026-03-08T13:08:17.822477+00:00",
            "download_path":"/mod/4165/MPFX/download/v0.2.0","changelog":"Profile support"}
         ]}
        """;

    private const string RealMpfxSearchJson = $"[{RealMpfxModJson}]";

    [Fact]
    public async Task SearchAsync_RealMpfxResponse_MapsTheListing()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(RealMpfxSearchJson), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("MPFX");

        var mod = Assert.Single(results);
        Assert.Equal("4165", mod.ModId); // Placeholder = SpaceDock's numeric id, stringified.
        Assert.Equal("spacedock", mod.Source);
        Assert.Equal(ContentType.Mod, mod.Type);
        Assert.Equal("MPFX", mod.Name);
        Assert.Equal(new[] { "AMPW" }, mod.Authors);
        Assert.Equal("Simple post processing", mod.Abstract);
        Assert.Equal("MIT", mod.License);
        // The oldest game version any release claims, not the newest:
        // v0.2.0 was built for 2026.3.3.3759.
        Assert.Equal("2026.3.3.3759", mod.GameMin);
        Assert.Equal("https://github.com/AMPW-german/MPFX", mod.Links["repository"]);
        Assert.Equal("https://spacedock.info/mod/4165/MPFX", mod.Links["spacedock"]);
        var releases = Assert.IsType<ReleaseSource>(mod.Releases);
        Assert.Equal("spacedock", releases.AuthorityHost.Host);
        Assert.Equal("4165", releases.AuthorityHost.Reference);
    }

    [Fact]
    public async Task SearchAsync_WebsiteOnKsaForums_BecomesTheForumsLink()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(RealMpfxSearchJson), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("MPFX");

        Assert.Equal("https://forums.ahwoo.com/threads/mpfx-v0-1-0-simple-post-processing-effects.812/", results[0].ForumUrl);
    }

    [Fact]
    public async Task SearchAsync_WebsiteElsewhere_SpaceDockPageStandsInAsForumsLink()
    {
        var json = """
            [{"name":"Mod","id":7,"game_id":22409,"author":"a","default_version_id":1,
              "website":"https://example.com/my-mod","url":"/mod/7/Mod",
              "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1,
                           "created":"2026-01-01T00:00:00+00:00","download_path":"/mod/7/Mod/download/1.0.0"}]}]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("Mod");

        Assert.Equal("https://spacedock.info/mod/7/Mod", results[0].ForumUrl);
        Assert.Equal("https://example.com/my-mod", results[0].Links["homepage"]);
    }

    [Fact]
    public async Task SearchAsync_MissingLicense_MapsToUnknownPlaceholder()
    {
        var json = """
            [{"name":"Mod","id":7,"game_id":22409,"author":"a","default_version_id":1,
              "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1}]}]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("Mod");

        Assert.Equal("Unknown", results[0].License);
    }

    [Fact]
    public async Task SearchAsync_UnparseableVersionData_StillSurfacesTheListing()
    {
        // A mod whose version data does not parse must survive as a listing.
        var json = """
            [{"name":"Weird Mod","id":9,"game_id":22409,"author":"a","default_version_id":1,
              "versions":[{"friendly_version":"not-a-version","game_version":"garbage","id":1}]}]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("Weird");

        var mod = Assert.Single(results);
        Assert.Equal("Weird Mod", mod.Name);
        Assert.Equal("garbage", mod.GameMin); // Raw string kept; evaluates as unknown downstream.
        Assert.Empty(mod.Dependencies); // Unknown, not none: SpaceDock carries no dependency data.
        Assert.Null(mod.Loader);
    }

    [Fact]
    public async Task SearchAsync_NoVersionsAtAll_StillSurfacesTheListing()
    {
        var json = """
            [{"name":"Empty Mod","id":11,"game_id":22409,"author":"a","default_version_id":0,"versions":[]}]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("Empty");

        Assert.Equal("unknown", Assert.Single(results).GameMin);
    }

    [Fact]
    public async Task SearchAsync_FiltersOutNonKsaGameId()
    {
        var json = """
            [
              {"name":"KSA Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
               "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1}]},
              {"name":"Other Game Mod","id":2,"game_id":99999,"author":"b","default_version_id":2,
               "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":2}]}
            ]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("query");

        var mod = Assert.Single(results);
        Assert.Equal("1", mod.ModId);
    }

    [Fact]
    public async Task GetAvailableModsAsync_RequestsCorrectUrlWithKsaGameId()
    {
        var client = FakeHttpMessageHandler.BuildClient(
            _ => FakeHttpMessageHandler.JsonResponse("""{"result":[],"count":0,"pages":0,"page":1}"""),
            out var handler);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        await repository.GetAvailableModsAsync();

        Assert.Contains("game_id=22409", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetLatestReleaseAsync_MapsTheNewestRelease()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(RealMpfxModJson), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var release = await repository.GetLatestReleaseAsync("4165");

        Assert.NotNull(release);
        Assert.Equal("4165", release!.ModId);
        Assert.Equal("spacedock", release.Source);
        Assert.Equal(ModVersion.Parse("0.3.1"), release.Version);
        Assert.Equal(ReleaseStatus.Stable, release.ReleaseStatus);
        Assert.Equal("2026.7.10.5056", release.GameMin);
        Assert.Equal(5056, release.GameMinRevision);
        Assert.Equal("https://spacedock.info/mod/4165/MPFX/download/v0.3.1", release.Download.Url);
        Assert.Null(release.Download.Sha256); // SpaceDock exposes no checksum; null means unverifiable.
        Assert.Null(release.Download.SizeBytes);
        Assert.Null(release.InstallSizeBytes);
        Assert.Equal("Built for KSA", release.Changelog);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 20, 2, 32, TimeSpan.Zero), release.ReleaseDate, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_UnusableDefaultVersion_FallsBackToNewestMappable()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":99,
             "versions":[
               {"friendly_version":"broken","game_version":"2026.1.1.1","id":99,
                "created":"2026-01-03T00:00:00+00:00","download_path":"/d/99"},
               {"friendly_version":"1.1.0","game_version":"2026.1.1.1","id":2,
                "created":"2026-01-02T00:00:00+00:00","download_path":"/d/2"},
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var release = await repository.GetLatestReleaseAsync("1");

        Assert.Equal(ModVersion.Parse("1.1.0"), release!.Version);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_DefaultOlderThanNewest_ReturnsNewest()
    {
        // default_version_id is author-selectable; the contract says newest.
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[
               {"friendly_version":"2.0.0","game_version":"2026.1.1.1","id":2,
                "created":"2026-01-02T00:00:00+00:00","download_path":"/d/2"},
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var release = await repository.GetLatestReleaseAsync("1");

        Assert.Equal(ModVersion.Parse("2.0.0"), release!.Version);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_PreReleaseVersion_MapsAsTesting()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[{"friendly_version":"v1.2.0-beta.1","game_version":"2026.1.1.1","id":1,
                          "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var release = await repository.GetLatestReleaseAsync("1");

        Assert.Equal(ModVersion.Parse("1.2.0-beta.1"), release!.Version);
        Assert.Equal(ReleaseStatus.Testing, release.ReleaseStatus);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_UnresolvableModId_ReturnsNullWithoutMakingRequest()
    {
        var called = false;
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            called = true;
            return FakeHttpMessageHandler.JsonResponse("{}");
        }, out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetLatestReleaseAsync("some-true-modid-never-registered");

        Assert.Null(result);
        Assert.False(called); // No resolver entry: short-circuit, do not hit the network.
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ResolverRegisteredModId_ResolvesToCorrectSpaceDockId()
    {
        var json = """
            {"name":"Mod","id":999,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1,
                          "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out var handler);
        var resolver = new SpaceDockResolver();
        resolver.Register("true-mod-id-from-mod-toml", 999);
        var repository = new SpaceDockModRepository(client, resolver);

        await repository.GetLatestReleaseAsync("true-mod-id-from-mod-toml");

        Assert.Contains("api/mod/999", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetReleaseAsync_MatchingVersionExists_ReturnsIt()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":10,
             "versions":[
               {"friendly_version":"2.0.0","game_version":"2026.1.1.1","id":10,
                "created":"2026-01-02T00:00:00+00:00","download_path":"/d/10"},
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":11,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/11"}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetReleaseAsync("1", ModVersion.Parse("1.0.0"));

        Assert.NotNull(result);
        Assert.Equal(ModVersion.Parse("1.0.0"), result!.Version);
        Assert.Equal("https://spacedock.info/d/11", result.Download.Url);
    }

    [Fact]
    public async Task GetReleaseAsync_NoMatchingVersion_ReturnsNull()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":10,
             "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":10,
                          "created":"2026-01-01T00:00:00+00:00","download_path":"/d/10"}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetReleaseAsync("1", ModVersion.Parse("9.9.9"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetReleaseAsync_CollidingVersionStrings_ResolvesToTheUsableRow()
    {
        // "1.0" and "1.0.0" normalize to the same ModVersion; resolution must
        // fall through the unmappable first row to the usable one.
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":2,
             "versions":[
               {"friendly_version":"1.0","game_version":"garbage","id":1,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"},
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":2,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/2"}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetReleaseAsync("1", ModVersion.Parse("1.0.0"));

        Assert.NotNull(result);
        Assert.Equal("https://spacedock.info/d/2", result!.Download.Url);
    }

    [Fact]
    public async Task GetAvailableVersionsAsync_CollapsesDuplicateNormalizedVersions()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[
               {"friendly_version":"1.0","game_version":"2026.1.1.1","id":1,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"},
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":2,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/2"}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var versions = await repository.GetAvailableVersionsAsync("1");

        Assert.Equal(new[] { ModVersion.Parse("1.0.0") }, versions);
    }

    [Fact]
    public async Task ReleaseAccessors_UnknownId_ReturnNullOn404()
    {
        // 404 means not available, not an exception.
        var client = FakeHttpMessageHandler.BuildClient(
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        Assert.Null(await repository.GetLatestReleaseAsync("99999999"));
        Assert.Null(await repository.GetReleaseAsync("99999999", ModVersion.Parse("1.0.0")));
        Assert.Empty(await repository.GetAvailableVersionsAsync("99999999"));
    }

    [Fact]
    public async Task GetReleaseAsync_UnparseableGameVersion_ReturnsNull()
    {
        // A release without a resolvable game revision cannot be stamped.
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":10,
             "versions":[{"friendly_version":"1.0.0","game_version":"0.24.2","id":10,
                          "created":"2026-01-01T00:00:00+00:00","download_path":"/d/10"}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetReleaseAsync("1", ModVersion.Parse("1.0.0"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAvailableVersionsAsync_ListsOnlyMappableReleases_NewestFirst()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1,
                "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"},
               {"friendly_version":"2.0.0","game_version":"2026.1.1.1","id":2,
                "created":"2026-01-02T00:00:00+00:00","download_path":"/d/2"},
               {"friendly_version":"garbage","game_version":"2026.1.1.1","id":3,
                "created":"2026-01-03T00:00:00+00:00","download_path":"/d/3"},
               {"friendly_version":"3.0.0","game_version":"not-ksa","id":4,
                "created":"2026-01-04T00:00:00+00:00","download_path":"/d/4"}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var versions = await repository.GetAvailableVersionsAsync("1");

        Assert.Equal(new[] { ModVersion.Parse("2.0.0"), ModVersion.Parse("1.0.0") }, versions);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("13.0", "13.0.0")]
    [InlineData("7", "7.0.0")]
    [InlineData("V2.0.0", "2.0.0")]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("1.2.3.4-rc1", "1.2.3-rc1")]
    [InlineData("1.2-beta", "1.2.0-beta")]
    public async Task VersionNormalization_HandlesRealWorldFormats(string friendlyVersion, string expectedNormalized)
    {
        var json = $$"""
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[{"friendly_version":"{{friendlyVersion}}","game_version":"2026.1.1.1","id":1,
                          "created":"2026-01-01T00:00:00+00:00","download_path":"/d/1"}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var release = await repository.GetLatestReleaseAsync("1");

        Assert.Equal(ModVersion.Parse(expectedNormalized), release!.Version);
    }

    [Fact]
    public async Task Constructor_NullArguments_Throw()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse("{}"), out _);

        Assert.Throws<ArgumentNullException>(() => new SpaceDockModRepository(null!, new SpaceDockResolver()));
        Assert.Throws<ArgumentNullException>(() => new SpaceDockModRepository(client, null!));
    }
}
