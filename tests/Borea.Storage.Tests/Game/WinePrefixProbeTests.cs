using Borea.Core.Game;
using Borea.Storage.Game;
using Borea.Storage.Tests.Launch;

namespace Borea.Storage.Tests.Game;

public sealed class WinePrefixProbeTests : IDisposable
{
    private const string NoDriveNames = "Windows cannot name a file 'c:', so the drive links exist only on Linux and macOS.";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly WinePrefixProbe _probe = new(OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs);
    private readonly List<string> _links = [];

    private string Applications => Path.Combine(_tempRoot, "Applications");

    private static string Game(string prefix) =>
        Directory.CreateDirectory(Path.Combine(prefix, "drive_c", "Program Files", "Kitten Space Agency")).FullName;

    /// <summary>A prefix as Wine creates it, with c: on drive_c and z: on the host root.</summary>
    private string LinkedPrefix(string prefix)
    {
        WineFixtures.Prefix(prefix);
        _links.Add(WineFixtures.LinkDrive(prefix, 'c', "../drive_c"));
        _links.Add(WineFixtures.LinkDrive(prefix, 'z', "/"));
        return prefix;
    }

    [UnixFact(NoDriveNames)]
    public void Find_GameInDriveC_FindsThePrefixAndMapsTheGame()
    {
        var prefix = LinkedPrefix(Path.Combine(_tempRoot, "prefix"));
        var game = Game(prefix);

        var install = _probe.Find(game)!;

        Assert.Equal("prefix", Path.GetFileName(install.PrefixRoot));
        Assert.Equal(new[] { 'c', 'z' }, install.Drives.Drives.Keys.Order());
        Assert.Equal("/", install.Drives.Drives['z']);
        Assert.Null(install.Wrapper);
        Assert.Equal(@"C:\Program Files\Kitten Space Agency", _probe.ToWindowsPath(install, game));
        Assert.StartsWith(@"Z:\", _probe.ToWindowsPath(install, _tempRoot));
    }

    [UnixFact(NoDriveNames)]
    public void Find_ThroughTheDriveCLinkOfTheWrapper_FindsThePrefix()
    {
        var prefix = LinkedPrefix(WineFixtures.WrapperPrefix(Applications));
        Game(prefix);
        var contents = Path.GetDirectoryName(Path.GetDirectoryName(prefix))!;
        _links.Add(Directory.CreateSymbolicLink(Path.Combine(contents, "drive_c"), Path.Combine("SharedSupport", "prefix", "drive_c")).FullName);
        var linkedGame = Path.Combine(contents, "drive_c", "Program Files", "Kitten Space Agency");

        var install = _probe.Find(linkedGame)!;

        Assert.EndsWith(Path.Combine("SharedSupport", "prefix"), install.PrefixRoot);
        Assert.Equal(@"C:\Program Files\Kitten Space Agency", _probe.ToWindowsPath(install, linkedGame));
    }

    [UnixFact(NoDriveNames)]
    public void Find_DriveTableWithoutZ_LeavesAPathOutsideDriveCUnmapped()
    {
        var prefix = WineFixtures.Prefix(Path.Combine(_tempRoot, "prefix"));
        _links.Add(WineFixtures.LinkDrive(prefix, 'c', "../drive_c"));
        var outside = Directory.CreateDirectory(Path.Combine(prefix, "games", "KSA")).FullName;

        var install = _probe.Find(outside)!;

        Assert.Equal(new[] { 'c' }, install.Drives.Drives.Keys);
        Assert.Null(_probe.ToWindowsPath(install, outside));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Find_FolderWithDriveCButWithoutTheWineFiles_IsNotAPrefix(bool dosdevices, bool systemRegistry)
    {
        var prefix = WineFixtures.Prefix(Path.Combine(_tempRoot, "prefix"), dosdevices, systemRegistry);

        Assert.Null(_probe.Find(Game(prefix)));
    }

    [Fact]
    public void Find_PrefixWithoutDriveLinks_IsAPrefixWithoutDrives()
    {
        var prefix = WineFixtures.Prefix(Path.Combine(_tempRoot, "prefix"));

        var install = _probe.Find(Game(prefix))!;

        Assert.Empty(install.Drives.Drives);
    }

    [Theory]
    [InlineData(OsPlatform.Windows)]
    [InlineData(null)]
    public void Find_HostThatIsNotLinuxOrMacOs_FindsNothing(OsPlatform? platform)
    {
        var prefix = WineFixtures.Prefix(Path.Combine(_tempRoot, "prefix"));

        Assert.Null(new WinePrefixProbe(platform).Find(Game(prefix)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sikarugir")]
    public void Find_WrapperWithAnXmlInfoPlistAndItsLauncher_RecognizesTheWrapper(string toolFolder)
    {
        var prefix = WineFixtures.Prefix(WineFixtures.WrapperPrefix(Path.Combine(Applications, toolFolder)));
        var bundle = WineFixtures.Wrapper(prefix);

        var wrapper = _probe.Find(Game(prefix))!.Wrapper!;

        Assert.Equal(Path.GetFileName(bundle), Path.GetFileName(wrapper.BundlePath));
        Assert.Equal(Path.Combine(wrapper.BundlePath, "Contents", "MacOS", "launcher"), wrapper.LauncherPath);
    }

    public static TheoryData<string?, string?> WrappersThatAreNotRecognized => new()
    {
        { null, "launcher" },
        { "bplist00\u00d1\u0001\u0002_\u0010\u0012CFBundleExecutable", "launcher" },
        { WineFixtures.InfoPlist, null },
        { WineFixtures.InfoPlist.Replace("<string>launcher</string>", "<string>../MacOS/launcher</string>"), "launcher" },
        { WineFixtures.InfoPlist.Replace("Program Name and Path", "Program Flags"), "launcher" },
        { "<plist version=\"1.0\"><array/></plist>", "launcher" },
    };

    [Theory]
    [MemberData(nameof(WrappersThatAreNotRecognized))]
    public void Find_WrapperWithoutAUsableInfoPlistOrLauncher_IsAPlainPrefix(string? infoPlist, string? launcher)
    {
        var prefix = WineFixtures.Prefix(WineFixtures.WrapperPrefix(Applications));
        WineFixtures.Wrapper(prefix, infoPlist, launcher);

        var install = _probe.Find(Game(prefix))!;

        Assert.Null(install.Wrapper);
    }

    [Fact]
    public void Find_PrefixThatIsNotInsideAnApp_HasNoWrapper()
    {
        var prefix = WineFixtures.Prefix(Path.Combine(_tempRoot, "Kitten Space Agency", "Contents", "SharedSupport", "prefix"));
        WineFixtures.Wrapper(prefix);

        Assert.Null(_probe.Find(Game(prefix))?.Wrapper);
    }

    [Fact]
    public void Find_RelativePath_FindsNothing()
    {
        Assert.Null(_probe.Find(Path.Combine("prefix", "drive_c")));
    }

    public void Dispose() => WineFixtures.Delete(_tempRoot, _links);
}
