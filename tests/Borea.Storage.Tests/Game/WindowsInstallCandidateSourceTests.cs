using Borea.Storage.Game;

namespace Borea.Storage.Tests.Game;

public sealed class WindowsInstallCandidateSourceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private static readonly UninstallEntry[] Registry =
    [
        new("{BA65F5AF-CD20-48A4-A957-410CB9660FF7}_is1", "Kitten Space Agency", @"C:\Games\KSA\"),
        new("{A6C53918-1F1B-4C7B-AF34-63C696A3E822}_is1", "StarMap version 0.4.6", @"C:\Program Files (x86)\StarMap\"),
        new("{00000000-0000-0000-0000-000000000000}", "Some Other App", @"C:\Other"),
        new("StarMapWithoutLocation", "StarMap version 0.4.5", null),
    ];

    [Fact]
    public void GetGameDirectories_ReadsTheInstallLocationOfTheGameInstaller()
    {
        var source = new WindowsInstallCandidateSource(() => Registry, [], modsFolder: null);

        Assert.Equal([@"C:\Games\KSA\"], source.GetGameDirectories());
    }

    [Theory]
    [InlineData("Ahwoo App 019f01fc-623a-773f-8d70-f0177b16a20c")]
    [InlineData("AHWOO APP 019F01FC-623A-773F-8D70-F0177B16A20C")]
    public void GetGameDirectories_ReadsTheInstallLocationOfTheAhwooGamePackageOnly(string keyName)
    {
        UninstallEntry[] registry =
        [
            new("Ahwoo App 019fab3e-7ac8-7228-ba27-aca60c741e91", "Gruntz", @"F:\Games\Ahwoo\Games\Gruntz"),
            new(keyName, "Kitten Space Agency", @"F:\Games\Ahwoo\Games\Kitten Space Agency"),
            new("Ahwoo App 00000000-0000-0000-0000-000000000000", "Kitten Space Agency Dedicated Server", @"F:\Games\Ahwoo\Games\KSA Server"),
        ];
        var source = new WindowsInstallCandidateSource(() => registry, [], modsFolder: null);

        Assert.Equal([@"F:\Games\Ahwoo\Games\Kitten Space Agency"], source.GetGameDirectories());
    }

    [Fact]
    public void GetGameDirectories_InstallerAndAhwooEntry_ReadsBothFolders()
    {
        UninstallEntry[] registry =
        [
            .. Registry,
            new("Ahwoo App 019f01fc-623a-773f-8d70-f0177b16a20c", "Kitten Space Agency", @"F:\Games\Ahwoo\Games\Kitten Space Agency"),
        ];
        var source = new WindowsInstallCandidateSource(() => registry, [], modsFolder: null);

        Assert.Equal([@"C:\Games\KSA\", @"F:\Games\Ahwoo\Games\Kitten Space Agency"], source.GetGameDirectories());
    }

    [Fact]
    public void GetLoaderDirectories_ReadsStarMapEntriesTheFixedFolderAndEveryModsSubfolder()
    {
        var mods = Path.Combine(_tempRoot, "mods");
        var unpacked = Directory.CreateDirectory(Path.Combine(mods, "StarMap-0.4.6")).FullName;
        var source = new WindowsInstallCandidateSource(() => Registry, [@"C:\Program Files\StarMap"], mods);

        Assert.Equal(
            [@"C:\Program Files (x86)\StarMap\", @"C:\Program Files\StarMap", unpacked],
            source.GetLoaderDirectories());
    }

    [Fact]
    public void GetLoaderDirectories_ModsFolderMissing_ReturnsTheOtherCandidates()
    {
        var source = new WindowsInstallCandidateSource(() => [], [@"C:\Program Files\StarMap"], Path.Combine(_tempRoot, "missing"));

        Assert.Equal([@"C:\Program Files\StarMap"], source.GetLoaderDirectories());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
