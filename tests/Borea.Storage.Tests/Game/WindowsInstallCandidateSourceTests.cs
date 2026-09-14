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
