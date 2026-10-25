using Borea.Core.Game;
using Borea.Storage.Game;
using Borea.Storage.ModLoaders;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Launch;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Game;

public sealed class WrapperInstallCandidateSourceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly List<string> _links = [];

    private string Applications => Path.Combine(_tempRoot, "Applications");

    private string Prefix => WineFixtures.WrapperPrefix(Applications);

    private string DriveC => Path.Combine(Prefix, "drive_c");

    private string GameDirectory => Path.Combine(DriveC, "Program Files", "Kitten Space Agency");

    /// <summary>A wrapper whose prefix holds the fixture system.reg.</summary>
    private void PlaceWrapper(string? prefix = null)
    {
        prefix ??= Prefix;
        WineFixtures.Prefix(prefix);
        File.Copy(WineRegistryFileTests.Fixture, Path.Combine(prefix, "system.reg"), overwrite: true);
    }

    private static FakeWineProbe Probe(string prefix) =>
        new(new WineInstall(prefix, WineFixtures.Drives(new Dictionary<char, string> { ['c'] = Path.Combine(prefix, "drive_c") }), Wrapper: null));

    private WrapperInstallCandidateSource Source(IWinePrefixProbe? probe = null) => new(
        [Applications, Path.Combine(_tempRoot, "missing")],
        probe ?? Probe(Prefix));

    [Fact]
    public void GetGameDirectories_ReadsTheGameUninstallEntryOfTheWrapperAsAHostPath()
    {
        PlaceWrapper();

        Assert.Equal([GameDirectory], Source().GetGameDirectories());
    }

    [Fact]
    public void GetLoaderDirectories_ReadsTheStarMapEntriesAndTheProgramFilesFoldersAsHostPaths()
    {
        PlaceWrapper();

        Assert.Equal(
            [
                Path.Combine(DriveC, "Users", "J\u00fcrgen", "StarMap"),
                Path.Combine(DriveC, "Program Files", "StarMap"),
                Path.Combine(DriveC, "Program Files (x86)", "StarMap"),
            ],
            Source().GetLoaderDirectories());
    }

    [Fact]
    public void GetGameDirectories_ReadsTheHiveOfThePrefixUserToo()
    {
        WineFixtures.Prefix(Prefix);
        File.WriteAllText(Path.Combine(Prefix, "user.reg"), string.Join('\n',
            "WINE REGISTRY Version 2",
            "",
            @"[Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{BA65F5AF-CD20-48A4-A957-410CB9660FF7}_is1] 1758900000",
            @"""InstallLocation""=""C:\\Games\\KSA""",
            ""));

        Assert.Equal([Path.Combine(DriveC, "Games", "KSA")], Source().GetGameDirectories());
    }

    [Fact]
    public void GetGameDirectories_ReadsTheUninstallKeyOf32BitInstallersToo()
    {
        WineFixtures.Prefix(Prefix);
        File.WriteAllText(Path.Combine(Prefix, "system.reg"), string.Join('\n',
            "WINE REGISTRY Version 2",
            "",
            @"[Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{BA65F5AF-CD20-48A4-A957-410CB9660FF7}_is1] 1758900000",
            @"""InstallLocation""=""C:\\Games\\KSA""",
            ""));

        Assert.Equal([Path.Combine(DriveC, "Games", "KSA")], Source().GetGameDirectories());
    }

    [Fact]
    public void Candidates_AppWithoutAPrefixOrWithoutARecognizedPrefix_OfferNothing()
    {
        Directory.CreateDirectory(Path.Combine(Applications, "Other.app", "Contents", "MacOS"));
        WineFixtures.Prefix(WineFixtures.WrapperPrefix(Applications, "Unrecognized"));
        File.Copy(WineRegistryFileTests.Fixture, Path.Combine(WineFixtures.WrapperPrefix(Applications, "Unrecognized"), "system.reg"), overwrite: true);

        var source = Source();

        Assert.Empty(source.GetGameDirectories());
        Assert.Empty(source.GetLoaderDirectories());
    }

    [Theory]
    [InlineData("Sikarugir")]
    [InlineData("Kegworks")]
    [InlineData("Wineskin")]
    public void GetGameDirectories_WrapperInTheFolderOfAWrapperTool_ReadsItsGame(string tool)
    {
        var prefix = WineFixtures.WrapperPrefix(Path.Combine(Applications, tool), "KSA");
        PlaceWrapper(prefix);

        Assert.Equal([Path.Combine(prefix, "drive_c", "Program Files", "Kitten Space Agency")], Source(Probe(prefix)).GetGameDirectories());
    }

    [Fact]
    public void Candidates_WrapperTwoFoldersDeepOrInsideAnApp_AreNotProbed()
    {
        PlaceWrapper(WineFixtures.WrapperPrefix(Path.Combine(Applications, "Games", "Wine"), "KSA"));
        PlaceWrapper(WineFixtures.WrapperPrefix(Path.Combine(Applications, "Other.app"), "KSA"));
        var probe = Probe(Prefix);

        Assert.Empty(Source(probe).GetGameDirectories());
        Assert.Empty(probe.Probed);
    }

    [Fact]
    public async Task InstallDetector_FindsTheGameInTheWrapper()
    {
        PlaceWrapper();
        PlaceGame(GameDirectory);
        var probe = new FakeWineProbe(new WineInstall(Prefix, WineFixtures.Drives(new Dictionary<char, string> { ['c'] = DriveC }), Wrapper: null));

        var detection = await Detector(Source(probe), probe).DetectAsync([]);

        var game = Assert.Single(detection.Games);
        Assert.Equal(GameDirectory, game.Directory);
        Assert.Equal("2026.8.3.5117", game.Version.RawVersion);
    }

    [UnixFact("Windows cannot name a file 'c:', so the drive links exist only on Linux and macOS.")]
    public async Task InstallDetector_FindsTheGameInARealWrapperPrefix()
    {
        PlaceWrapper();
        _links.Add(WineFixtures.LinkDrive(Prefix, 'c', "../drive_c"));
        _links.Add(WineFixtures.LinkDrive(Prefix, 'z', "/"));
        PlaceGame(GameDirectory);
        var probe = new WinePrefixProbe(OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs);

        var detection = await Detector(Source(probe), probe).DetectAsync([]);

        var game = Assert.Single(detection.Games);
        Assert.EndsWith(Path.Combine("SharedSupport", "prefix", "drive_c", "Program Files", "Kitten Space Agency"), game.Directory);
    }

    [UnixFact("Windows cannot name a file 'c:', so the drive links exist only on Linux and macOS.")]
    public async Task InstallDetector_FindsTheGameAndTheWrapperInAWrapperToolFolder()
    {
        var prefix = WineFixtures.WrapperPrefix(Path.Combine(Applications, "Sikarugir"), "KSA");
        PlaceWrapper(prefix);
        WineFixtures.Wrapper(prefix);
        _links.Add(WineFixtures.LinkDrive(prefix, 'c', "../drive_c"));
        _links.Add(WineFixtures.LinkDrive(prefix, 'z', "/"));
        PlaceGame(Path.Combine(prefix, "drive_c", "Program Files", "Kitten Space Agency"));
        var probe = new WinePrefixProbe(OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs);

        var detection = await Detector(Source(probe), probe).DetectAsync([]);

        var game = Assert.Single(detection.Games);
        Assert.EndsWith(Path.Combine("Sikarugir", "KSA.app"), probe.Find(game.Directory)?.Wrapper?.BundlePath);
    }

    private InstallDetector Detector(IInstallCandidateSource source, IWinePrefixProbe probe)
    {
        var paths = new TestGamePathProvider(_tempRoot);
        var adopter = new FileLoaderAdopter(new FileBoreaSettingsRepository(paths), new LoaderConfigurator(probe));
        return new InstallDetector(source, adopter, paths.GetLoadersRoot(), paths.GetGameBuildsRoot());
    }

    private static void PlaceGame(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "KSA.exe"), "game");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(directory, "KSA.dll"));
    }

    public void Dispose() => WineFixtures.Delete(_tempRoot, _links);
}
