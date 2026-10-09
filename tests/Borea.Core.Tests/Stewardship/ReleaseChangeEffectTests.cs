using System.Text.Json.Nodes;
using Borea.Core.Mods;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

/// <summary>What a change does to the release files before the preview, and the values that the files state now.</summary>
public sealed class ReleaseChangeEffectTests
{
    private static readonly JsonObject Vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "amendment-vectors.json")))!.AsObject();

    private static readonly IReadOnlyList<string> GameVersions = [.. Vectors["game_versions"]!.AsArray().Select(version => (string)version!)];

    /// <summary>ExampleMod 1.2.0 for game 2026.8.3.5117 to 2026.8.19.5261, StarMap from 0.4.5, and ExampleLibrary 2.0.0 to 2.9.0.</summary>
    private static readonly string Base = (string)Vectors["vectors"]!.AsArray().Single(vector => (string)vector!["name"]! == "the owner raises a game_max")!["base"]!;

    private static readonly ReleaseFile File120 = new("1.2.0", ReleaseAmendment.PathOf("ExampleMod", "1.2.0"), Base);

    /// <summary>ExampleMod 1.1.0, which goes up to 2026.8.22.5348.</summary>
    private static readonly ReleaseFile File110 = new("1.1.0", ReleaseAmendment.PathOf("ExampleMod", "1.1.0"),
        Base.Replace("\"version\": \"1.2.0\"", "\"version\": \"1.1.0\"", StringComparison.Ordinal)
            .Replace("\"game_max\": \"2026.8.19.5261\",\n  \"game_max_revision\": 5261", "\"game_max\": \"2026.8.22.5348\",\n  \"game_max_revision\": 5348", StringComparison.Ordinal));

    [Theory]
    [InlineData("2026.8.22.5348", ReleaseChangeEffect.Widens)]
    [InlineData("2026.8.5.5168", ReleaseChangeEffect.Narrows)]
    [InlineData("2026.8.19.5261", ReleaseChangeEffect.Unchanged)]
    [InlineData("soon", ReleaseChangeEffect.Refused)]
    public void GameMax_OfOneRelease_WidensNarrowsOrSaysWhatTheFileSays(string gameMax, ReleaseChangeEffect effect) =>
        Assert.Equal(effect, ReleaseAmendment.EffectOf(new ReleaseChange { GameMax = gameMax }, [File120], GameVersions, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("2026.8.22.5348", ReleaseChangeEffect.Widens)]
    [InlineData("2026.8.19.5261", ReleaseChangeEffect.Narrows)]
    public void GameMax_OfSeveralReleases_WidensWhenItWidensOne(string gameMax, ReleaseChangeEffect effect) =>
        Assert.Equal(effect, ReleaseAmendment.EffectOf(new ReleaseChange { GameMax = gameMax }, [File120, File110], GameVersions, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData(null, "3.0.0", ReleaseChangeEffect.Widens)]
    [InlineData("2.1.0", null, ReleaseChangeEffect.Narrows)]
    [InlineData("1.0.0", null, ReleaseChangeEffect.Widens)]
    public void ADependencyBound_UsesTheSameChecksAsThePreview(string? min, string? max, ReleaseChangeEffect effect)
    {
        var change = new ReleaseChange { DependencyBounds = [new ReleaseDependencyBounds("ExampleLibrary", min, max)] };

        Assert.Equal(effect, ReleaseAmendment.EffectOf(change, [File120], GameVersions, DateTimeOffset.UtcNow));
        Assert.Equal(effect == ReleaseChangeEffect.Widens, ReleaseAmendment.Create(change, GameVersions, DateTimeOffset.UtcNow).Apply(File120.Path, File120.Text, ReleaseAmender.Owner)!.Widens);
    }

    /// <summary>
    /// The removal of a declared dependency widens once the mod.toml of each archive was read and does not declare it. A derived one stays, and a release
    /// that does not state it stays as it is.
    /// </summary>
    [Theory]
    [InlineData("ExampleLibrary", "KittenExtensions", ReleaseChangeEffect.Widens)]
    [InlineData("ExampleLibrary", "KittenExtensions,ExampleLibrary", ReleaseChangeEffect.Refused)]
    [InlineData("ExampleLibrary", null, ReleaseChangeEffect.Refused)]
    [InlineData("KittenExtensions", "KittenExtensions", ReleaseChangeEffect.Refused)]
    [InlineData("Nothing", "KittenExtensions", ReleaseChangeEffect.Unchanged)]
    public void ARemovedDependency_UsesTheSameChecksAsThePreview(string id, string? modToml, ReleaseChangeEffect effect)
    {
        IReadOnlyList<LocalModDependency> declared = modToml is null ? [] : [.. modToml.Split(',').Select(name => new LocalModDependency(name, name == "KittenExtensions"))];
        var read = modToml is null ? null : new Dictionary<string, IReadOnlyList<LocalModDependency>> { [File120.Path] = declared, [File110.Path] = declared };

        Assert.Equal(effect, ReleaseAmendment.EffectOf(new ReleaseChange { RemovedDependencies = [id] }, [File120, File110], GameVersions, DateTimeOffset.UtcNow, read));
    }

    [Fact]
    public void Values_AreTheBoundsTheFileStates_AndNoJsonGivesNone()
    {
        var values = ReleaseFileValues.Read(Base)!;

        Assert.Equal(("2026.8.3.5117", "2026.8.19.5261"), (values.GameMin, values.GameMax));
        Assert.Equal(new ReleaseFileLoader("StarMap", "0.4.5", null), values.Loader);
        Assert.Equal(new ReleaseFileDependency("ExampleLibrary", "required", "2.0.0", "2.9.0", "authored"), values.Dependency("examplelibrary"));
        Assert.Equal(new ReleaseFileDependency("KittenExtensions", "optional", null, null, "derived"), values.Dependency("KittenExtensions"));
        Assert.Equal((true, false), (values.Dependency("ExampleLibrary")!.IsAuthored, values.Dependency("KittenExtensions")!.IsAuthored));
        Assert.Null(values.Dependency("Nothing"));
        Assert.Null(ReleaseFileValues.Read("not json"));
        Assert.Null(ReleaseFileValues.Read("[1]"));
    }
}
