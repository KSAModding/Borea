using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Storage.Files;
using Borea.Storage.Game;
using Borea.Storage.Paths;

namespace Borea.Storage.Tests.Game;

public sealed class InstanceGameResolverTests : IDisposable
{
    private const string RealBuildFixture = "GameVersionFixture.dll";

    private static readonly GameVersion FixtureVersion = GameVersion.Parse("2026.8.3.5117");

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private string SettingsGame => Path.Combine(_tempRoot, "Game");

    private string Library => Path.Combine(_tempRoot, "Library");

    [Fact]
    public void Resolve_InstanceWithoutABuild_UsesTheGameFolderOfTheSettings()
    {
        PlaceGameAssembly(SettingsGame);

        var game = Resolver().Resolve(new Instance("Career", InstanceSource.Custom.Value));

        Assert.Null(game.KeptBuild);
        Assert.Equal(SettingsGame, game.GameDirectory);
        Assert.Equal(FixtureVersion, game.Version?.Version);
        Assert.Equal(KeptBuildProblem.None, game.Problem);
    }

    [Fact]
    public void Resolve_KeptBuildWithItsFolder_UsesTheKeptBuild()
    {
        var kept = Path.Combine(Library, "Game Builds", "2026.8.3.5117");
        PlaceGameAssembly(kept);

        var game = Resolver().Resolve(Keeping(FixtureVersion));

        Assert.Equal(FixtureVersion, game.KeptBuild);
        Assert.Equal(kept, game.GameDirectory);
        Assert.Equal(FixtureVersion, game.Version?.Version);
        Assert.Equal(KeptBuildProblem.None, game.Problem);
    }

    [Theory]
    [InlineData(KeptBuildProblem.Missing)]
    [InlineData(KeptBuildProblem.WrongVersion)]
    public void Resolve_KeptBuildMissingOrOfAnotherVersion_GivesTheProblemAndNoFolder(KeptBuildProblem expected)
    {
        // the settings folder holds a good build, so a fallback to it would show
        PlaceGameAssembly(SettingsGame);
        var build = GameVersion.Parse("2026.10.13.5602");
        if (expected == KeptBuildProblem.WrongVersion)
            PlaceGameAssembly(Path.Combine(Library, "Game Builds", "2026.10.13.5602"));

        var game = Resolver().Resolve(Keeping(build));

        Assert.Equal(build, game.KeptBuild);
        Assert.Null(game.GameDirectory);
        Assert.Null(game.Version);
        Assert.Equal(expected, game.Problem);
    }

    private InstanceGameResolver Resolver()
    {
        var paths = new GamePathProvider(SettingsGame, boreaRoot: Path.Combine(_tempRoot, "Borea"), libraryFolder: Library);
        return new InstanceGameResolver(paths, new InstalledGameVersionProvider(paths));
    }

    private static Instance Keeping(GameVersion build)
    {
        var instance = new Instance("Career", InstanceSource.Custom.Value);
        instance.SetGameBuild(build);
        return instance;
    }

    private static void PlaceGameAssembly(string folder)
    {
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory, RealBuildFixture), Path.Combine(folder, "KSA.dll"));
    }

    public void Dispose() => DirectoryLinks.DeleteTreeWithoutFollowingLinks(_tempRoot);
}
