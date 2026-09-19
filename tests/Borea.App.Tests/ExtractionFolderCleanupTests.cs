using System.Diagnostics;
using Borea.Core.Logging;

namespace Borea.App.Tests;

public sealed class ExtractionFolderCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaExtractionTest_" + Guid.NewGuid());
    private readonly RecordingLog _log = new();
    private readonly List<string> _links = [];

    private string ApplicationFolder => Path.Combine(_root, ".net", "borea");

    [Fact]
    public void Run_OtherBuildFolders_AreDeletedAndTheOwnFolderStays()
    {
        var own = Build("Own", bytes: 10);
        var older = Build("Older", bytes: 100);
        var oldest = Build("Oldest", bytes: 1000);
        Directory.CreateDirectory(Path.Combine(oldest, "runtimes"));
        File.WriteAllBytes(Path.Combine(oldest, "runtimes", "native.so"), new byte[5]);
        Age(oldest);
        var file = Path.Combine(ApplicationFolder, "note.txt");
        File.WriteAllText(file, "x");

        var removed = Cleanup().Run(ApplicationFolder, own);

        Assert.Equal(2, removed);
        Assert.True(Directory.Exists(own));
        Assert.True(File.Exists(file));
        Assert.False(Directory.Exists(older));
        Assert.False(Directory.Exists(oldest));
        Assert.Equal("Extraction folders of other Borea builds removed: 2, 1105 bytes.", Assert.Single(_log.Messages));
    }

    [Fact]
    public void Run_OnlyTheOwnFolder_WritesNoLogLine()
    {
        var own = Build("Own", bytes: 10);

        Assert.Equal(0, Cleanup().Run(ApplicationFolder, own));

        Assert.True(Directory.Exists(own));
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void Run_OwnFolderUnknown_DeletesNothing()
    {
        var older = Build("Older", bytes: 100);

        Assert.Equal(0, Cleanup().Run(ApplicationFolder, ownFolder: null));

        Assert.True(Directory.Exists(older));
        Assert.Empty(_log.Messages);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("nested")]
    [InlineData("missing")]
    public void Run_OwnFolderNotDirectlyInTheApplicationFolder_DeletesNothing(string where)
    {
        var older = Build("Older", bytes: 100);
        var own = where switch
        {
            "outside" => Directory.CreateDirectory(Path.Combine(_root, "elsewhere", "Own")).FullName,
            "nested" => Directory.CreateDirectory(Path.Combine(older, "Own")).FullName,
            _ => Path.Combine(ApplicationFolder, "Gone"),
        };

        Assert.Equal(0, Cleanup().Run(ApplicationFolder, own));

        Assert.True(Directory.Exists(older));
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void Run_OtherBoreaRunning_DeletesNothing()
    {
        var own = Build("Own", bytes: 10);
        var older = Build("Older", bytes: 100);

        Assert.Equal(0, new ExtractionFolderCleanup(_log, isOtherInstanceRunning: () => true).Run(ApplicationFolder, own));

        Assert.True(Directory.Exists(older));
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void Run_OtherBoreaStartsDuringTheRun_StopsDeleting()
    {
        var own = Build("Own", bytes: 10);
        var older = Build("Older", bytes: 100);
        var oldest = Build("Oldest", bytes: 100);
        var checks = 0;

        Assert.Equal(1, new ExtractionFolderCleanup(_log, () => ++checks > 1).Run(ApplicationFolder, own));

        Assert.Single(new[] { older, oldest }, Directory.Exists);
        Assert.Equal("Extraction folders of other Borea builds removed: 1, 100 bytes.", Assert.Single(_log.Messages));
    }

    [Fact]
    public void Run_RecentlyWrittenFolder_Stays()
    {
        var own = Build("Own", bytes: 10);
        var older = Build("Older", bytes: 100);
        var extracting = Directory.CreateDirectory(Path.Combine(ApplicationFolder, "1f2c")).FullName;
        File.WriteAllBytes(Path.Combine(extracting, "native.dll"), new byte[1000]);

        Assert.Equal(1, Cleanup().Run(ApplicationFolder, own));

        Assert.True(Directory.Exists(extracting));
        Assert.False(Directory.Exists(older));
        Assert.Equal("Extraction folders of other Borea builds removed: 1, 100 bytes.", Assert.Single(_log.Messages));
    }

    [Fact]
    public void Run_FolderThatFailsToDelete_IsSkipped()
    {
        var own = Build("Own", bytes: 10);
        var inUse = Build("InUse", bytes: 100);
        var older = Build("Older", bytes: 1000);
        var cleanup = new ExtractionFolderCleanup(_log, () => false, folder =>
        {
            if (Path.GetFileName(folder) == "InUse")
                throw new IOException("The file is in use.");

            Directory.Delete(folder, recursive: true);
        });

        Assert.Equal(1, cleanup.Run(ApplicationFolder, own));

        Assert.True(Directory.Exists(inUse));
        Assert.False(Directory.Exists(older));
        Assert.Equal("Extraction folders of other Borea builds removed: 1, 1000 bytes.", Assert.Single(_log.Messages));
    }

    [WindowsFact("Only Windows refuses to delete a file that is open.")]
    public void Run_FolderWithAnOpenFile_IsSkipped()
    {
        var own = Build("Own", bytes: 10);
        var inUse = Build("InUse", bytes: 100);
        var older = Build("Older", bytes: 1000);

        using (new FileStream(Path.Combine(inUse, "native.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(1, Cleanup().Run(ApplicationFolder, own));

        Assert.True(Directory.Exists(inUse));
        Assert.False(Directory.Exists(older));
        Assert.Equal("Extraction folders of other Borea builds removed: 1, 1000 bytes.", Assert.Single(_log.Messages));
    }

    [Fact]
    public void Run_LinksInsideTheApplicationFolder_AreNotFollowed()
    {
        var own = Build("Own", bytes: 10);
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var targetFile = Path.Combine(target, "keep.bin");
        File.WriteAllBytes(targetFile, new byte[50]);
        var linkedBuild = Path.Combine(ApplicationFolder, "LinkedBuild");
        CreateDirectoryLink(linkedBuild, target);
        var older = Build("Older", bytes: 100);
        CreateDirectoryLink(Path.Combine(older, "link"), target);
        TryCreateFileLink(Path.Combine(older, "file-link"), targetFile);
        Age(older);

        Assert.Equal(1, Cleanup().Run(ApplicationFolder, own));

        Assert.False(Directory.Exists(older));
        Assert.True(Directory.Exists(linkedBuild));
        Assert.True(File.Exists(targetFile));
        Assert.Equal("Extraction folders of other Borea builds removed: 1, 100 bytes.", Assert.Single(_log.Messages));
    }

    [Fact]
    public void Run_ApplicationFolderIsALink_DeletesNothing()
    {
        var target = Path.Combine(_root, "target");
        Build("Own", bytes: 10, target);
        var older = Build("Older", bytes: 100, target);
        Directory.CreateDirectory(Path.GetDirectoryName(ApplicationFolder)!);
        CreateDirectoryLink(ApplicationFolder, target);

        Assert.Equal(0, Cleanup().Run(ApplicationFolder, Path.Combine(ApplicationFolder, "Own")));

        Assert.True(Directory.Exists(older));
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void FindOwnFolder_TheOneSearchFolderInsideTheApplicationFolder()
    {
        var own = Path.Combine(ApplicationFolder, "Own");
        var folders = string.Join(Path.PathSeparator, own + Path.DirectorySeparatorChar, Path.Combine(_root, "program"), "");

        Assert.Equal(own, ExtractionFolderCleanup.FindOwnFolder(ApplicationFolder, folders));
    }

    [Fact]
    public void FindOwnFolder_NoneOrSeveralOrRelative_IsUnknown()
    {
        var one = Path.Combine(ApplicationFolder, "One");
        var two = Path.Combine(ApplicationFolder, "Two");

        Assert.Null(ExtractionFolderCleanup.FindOwnFolder(ApplicationFolder, null));
        Assert.Null(ExtractionFolderCleanup.FindOwnFolder(ApplicationFolder, Path.Combine(_root, "program")));
        Assert.Null(ExtractionFolderCleanup.FindOwnFolder(ApplicationFolder, Path.Combine(one, "nested")));
        Assert.Null(ExtractionFolderCleanup.FindOwnFolder(ApplicationFolder, string.Join(Path.PathSeparator, one, two)));
        Assert.Null(ExtractionFolderCleanup.FindOwnFolder(ApplicationFolder, Path.Combine(".net", "borea", "One")));
    }

    [Theory]
    [InlineData("borea.exe", true, "borea")]
    [InlineData("Borea.EXE", true, "Borea")]
    [InlineData("borea", true, "borea")]
    [InlineData("borea.exe", false, "borea.exe")]
    [InlineData("borea", false, "borea")]
    public void ProgramName_DropsTheExeExtensionOnlyOnWindows(string file, bool windows, string expected)
    {
        Assert.Equal(expected, ExtractionFolderCleanup.ProgramName(Path.Combine(_root, file), windows));
    }

    public void Dispose()
    {
        foreach (var link in _links.Where(Path.Exists))
        {
            if (OperatingSystem.IsWindows())
                Directory.Delete(link);
            else
                File.Delete(link);
        }

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ExtractionFolderCleanup Cleanup() => new(_log, () => false);

    private string Build(string name, int bytes, string? parent = null)
    {
        var folder = Directory.CreateDirectory(Path.Combine(parent ?? ApplicationFolder, name)).FullName;
        File.WriteAllBytes(Path.Combine(folder, "native.dll"), new byte[bytes]);
        Age(folder);
        return folder;
    }

    private static void Age(string folder) => Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddDays(-1));

    // a junction needs no administrator rights on Windows, unlike a symbolic link
    private void CreateDirectoryLink(string link, string target)
    {
        _links.Add(link);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var start = new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(new DirectoryInfo(link).LinkTarget is not null, "The junction was not created.");
    }

    // Windows creates a file symbolic link only with administrator rights or in developer mode
    private static void TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class RecordingLog : IBoreaLog
    {
        public List<string> Messages { get; } = [];

        public string CurrentFilePath => "borea.log";

        public void Write(string message) => Messages.Add(message);

        public void Write(string message, Exception exception) => Messages.Add(message);

        public IReadOnlyList<string> ReadRecentLines(int maxLines) => [];
    }
}
