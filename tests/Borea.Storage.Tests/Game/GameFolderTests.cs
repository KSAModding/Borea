using Borea.Core.Game;
using Borea.Storage.Game;

namespace Borea.Storage.Tests.Game;

public sealed class GameFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    public GameFolderTests()
    {
        Directory.CreateDirectory(_root);
    }

    /// <summary>A real game assembly as KSA.dll in <paramref name="relative"/> below the root, with the program file when one is named.</summary>
    private string PlaceGame(string relative, string? program = "KSA.exe")
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, relative)).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(folder, "KSA.dll"));
        if (program is not null)
            File.WriteAllBytes(Path.Combine(folder, program), []);
        return folder;
    }

    [Theory]
    [InlineData("empty folder", true)]
    [InlineData("game folder", false)]
    [InlineData("missing folder", false)]
    [InlineData("not set", false)]
    public void LacksGame_SaysOnlyAnExistingFolderWithoutKsaDll(string folder, bool expected)
    {
        var path = folder switch
        {
            "empty folder" => Directory.CreateDirectory(Path.Combine(_root, "ahwoo")).FullName,
            "game folder" => PlaceGame("KSA"),
            "missing folder" => Path.Combine(_root, "gone"),
            _ => null,
        };

        Assert.Equal(expected, GameFolder.LacksGame(path));
    }

    [Theory]
    [InlineData("KSA")]
    [InlineData("Games/KSA")]
    public void FindBelow_OneGameUpToTwoLevelsDown_FindsItWithItsVersion(string relative)
    {
        var game = PlaceGame(relative);

        var found = GameFolder.FindBelow(_root, OsPlatform.Windows);

        Assert.Equal(game, found?.Directory);
        Assert.Equal("2026.8.3.5117", found?.Version.RawVersion);
    }

    [Fact]
    public void FindBelow_GameThreeLevelsDown_FindsNothing()
    {
        PlaceGame("Games/Ahwoo/KSA");

        Assert.Null(GameFolder.FindBelow(_root, OsPlatform.Windows));
    }

    [Fact]
    public void FindBelow_TwoGames_FindsNothing()
    {
        PlaceGame("KSA");
        PlaceGame("Games/KSA Copy");

        Assert.Null(GameFolder.FindBelow(_root, OsPlatform.Windows));
    }

    [Theory]
    [InlineData(OsPlatform.Linux, "KSA", true)]
    [InlineData(OsPlatform.Linux, "KSA.exe", true)]
    [InlineData(OsPlatform.Linux, null, false)]
    [InlineData(OsPlatform.Windows, "KSA.exe", true)]
    [InlineData(OsPlatform.Windows, "KSA", false)]
    [InlineData(OsPlatform.MacOs, "KSA", true)]
    [InlineData(OsPlatform.MacOs, "KSA.exe", true)]
    [InlineData(OsPlatform.MacOs, null, false)]
    public void FindBelow_NeedsTheProgramFileOfTheBuildNextToKsaDll(OsPlatform host, string? program, bool expected)
    {
        var game = PlaceGame("KSA", program);

        Assert.Equal(expected ? game : null, GameFolder.FindBelow(_root, host)?.Directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
