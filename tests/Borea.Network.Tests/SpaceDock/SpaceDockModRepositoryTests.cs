using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Network.SpaceDock;

namespace Borea.Network.Tests;

public sealed class SpaceDockModRepositoryTests
{
    // Trimmed/adapted from the real /api/search/mod response for "MPFX".
    private const string RealMpfxSearchJson = """
        [{"name":"MPFX","id":4165,"game":"Kitten Space Agency","game_id":22409,
          "short_description":"Simple post processing","author":"AMPW",
          "default_version_id":24490,"url":"/mod/4165/MPFX","website":null,
          "versions":[
            {"friendly_version":"v0.3.1","game_version":"2026.7.10.5056","id":24490,
             "created":"2026-07-23T20:02:32.835271+00:00",
             "download_path":"/mod/4165/MPFX/download/v0.3.1","changelog":"Built for KSA"},
            {"friendly_version":"v0.2.0","game_version":"2026.3.3.3759","id":23419,
             "created":"2026-03-08T13:08:17.822477+00:00",
             "download_path":"/mod/4165/MPFX/download/v0.2.0","changelog":"Profile support"}
          ]}]
        """;

    [Fact]
    public async Task SearchAsync_RealMpfxResponse_MapsCorrectly()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(RealMpfxSearchJson), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("MPFX");

        var mod = Assert.Single(results);
        Assert.Equal("4165", mod.ModId); // Placeholder = SpaceDock's numeric id, stringified.
        Assert.Equal("spacedock", mod.Source);
        Assert.Equal("MPFX", mod.Name);
        Assert.Equal("AMPW", mod.Author);
        Assert.Equal(ModVersion.Parse("0.3.1"), mod.Version); // "v0.3.1" -> stripped 'v'.
        Assert.Equal(GameVersion.Parse("2026.7.10.5056"), mod.BuiltForGameVersion);
        Assert.Equal("Built for KSA", mod.ChangeLog);
        Assert.Equal("https://spacedock.info/mod/4165/MPFX", mod.HomepageUrl);
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
    public async Task SearchAsync_MissingGameId_FallsBackToGameVersionHeuristic()
    {
        // No game_id field at all — KSP-shaped version should be excluded,
        // KSA-shaped version should be included, purely by GameVersion parse success.
        var json = """
            [
              {"name":"KSA-shaped","id":1,"author":"a","default_version_id":1,
               "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1}]},
              {"name":"KSP-shaped","id":2,"author":"b","default_version_id":2,
               "versions":[{"friendly_version":"1.0.0","game_version":"0.24.2","id":2}]}
            ]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("query");

        var mod = Assert.Single(results);
        Assert.Equal("KSA-shaped", mod.Name);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("13.0", "13.0.0")]
    [InlineData("7", "7.0.0")]
    [InlineData("V2.0.0", "2.0.0")]
    public async Task VersionNormalization_HandlesRealWorldFormats(string friendlyVersion, string expectedNormalized)
    {
        var json = $$"""
            [{"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
              "versions":[{"friendly_version":"{{friendlyVersion}}","game_version":"2026.1.1.1","id":1}]}]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("query");

        Assert.Equal(ModVersion.Parse(expectedNormalized), results[0].Version);
    }

    [Fact]
    public async Task VersionNormalization_UnparseableFriendlyVersion_ExcludesMod()
    {
        var json = """
            [{"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
              "versions":[{"friendly_version":"not-a-version-at-all","game_version":"2026.1.1.1","id":1}]}]
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var results = await repository.SearchAsync("query");

        Assert.Empty(results);
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
    public async Task GetLatestAsync_UnresolvableModId_ReturnsNullWithoutMakingRequest()
    {
        var called = false;
        var client = FakeHttpMessageHandler.BuildClient(_ =>
        {
            called = true;
            return FakeHttpMessageHandler.JsonResponse("{}");
        }, out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetLatestAsync("some-true-modid-never-registered");

        Assert.Null(result);
        Assert.False(called); // No resolver entry — should short-circuit, not hit the network.
    }

    [Fact]
    public async Task GetLatestAsync_NumericModId_UsedDirectlyAsSpaceDockId()
    {
        var json = """
            {"name":"Mod","id":42,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out var handler);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        await repository.GetLatestAsync("42");

        Assert.Contains("api/mod/42", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetLatestAsync_ResolverRegisteredModId_ResolvesToCorrectSpaceDockId()
    {
        var json = """
            {"name":"Mod","id":999,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out var handler);
        var resolver = new SpaceDockResolver();
        resolver.Register("true-mod-id-from-mod-toml", 999);
        var repository = new SpaceDockModRepository(client, resolver);

        await repository.GetLatestAsync("true-mod-id-from-mod-toml");

        Assert.Contains("api/mod/999", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetVersionAsync_MatchingVersionExists_ReturnsIt()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":10,
             "versions":[
               {"friendly_version":"2.0.0","game_version":"2026.1.1.1","id":10},
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":11}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetVersionAsync("1", ModVersion.Parse("1.0.0"));

        Assert.NotNull(result);
        Assert.Equal(ModVersion.Parse("1.0.0"), result!.Version);
    }

    [Fact]
    public async Task GetVersionAsync_NoMatchingVersion_ReturnsNull()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":10,
             "versions":[{"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":10}]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var result = await repository.GetVersionAsync("1", ModVersion.Parse("9.9.9"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAvailableVersionsAsync_SkipsUnparseableEntries()
    {
        var json = """
            {"name":"Mod","id":1,"game_id":22409,"author":"a","default_version_id":1,
             "versions":[
               {"friendly_version":"1.0.0","game_version":"2026.1.1.1","id":1},
               {"friendly_version":"garbage","game_version":"2026.1.1.1","id":2}
             ]}
            """;
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse(json), out _);
        var repository = new SpaceDockModRepository(client, new SpaceDockResolver());

        var versions = await repository.GetAvailableVersionsAsync("1");

        Assert.Single(versions);
        Assert.Equal(ModVersion.Parse("1.0.0"), versions[0]);
    }

    [Fact]
    public async Task Constructor_NullArguments_Throw()
    {
        var client = FakeHttpMessageHandler.BuildClient(_ => FakeHttpMessageHandler.JsonResponse("{}"), out _);

        Assert.Throws<System.ArgumentNullException>(() => new SpaceDockModRepository(null!, new SpaceDockResolver()));
        Assert.Throws<System.ArgumentNullException>(() => new SpaceDockModRepository(client, null!));
    }
}