using Borea.Core.Mods;
using Borea.Core.Sharing;
using Borea.Storage.Sharing;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Sharing;

public sealed class FileSharedContentRecordsTests : IDisposable
{
    private static readonly SharedContentEntry Rover = new(ContentType.Vehicle, "ksamods", "ksamods.gg", "ExampleRover", "Example Rover", "1.0.0", new Uri("https://ksamods.gg/vehicles/ExampleRover"));
    private static readonly SharedContentAuthor Maxi = new(["Maxi", "Cairn"], "CC-BY-4.0");

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileSharedContentRecords _records;

    public FileSharedContentRecordsTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _records = new FileSharedContentRecords(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task NoFile_ReadsAsEmpty()
    {
        Assert.Empty(await _records.ListAsync());
        Assert.Null(await _records.GetAuthorAsync());
    }

    [Fact]
    public async Task Record_SurvivesAFreshStore_WithAndWithoutAPage()
    {
        var save = Rover with { Type = ContentType.Save, Page = null };
        await _records.RecordAsync(Rover, Maxi);
        await _records.RecordAsync(save, author: null);

        var fresh = new FileSharedContentRecords(_paths);

        Assert.Equal([Rover, save], await fresh.ListAsync());
        var author = await fresh.GetAuthorAsync();
        Assert.Equal(Maxi.Authors, author!.Authors);
        Assert.Equal(Maxi.License, author.License);
    }

    [Fact]
    public async Task Record_OfTheSameFolderAndHost_ReplacesTheEntryAndGoesLast()
    {
        var otherHost = Rover with { HostKey = "ownhost", HostName = "own.example" };
        var newVersion = Rover with { FolderName = "EXAMPLE ROVER", Version = "1.1.0" };
        await _records.RecordAsync(Rover, author: null);
        await _records.RecordAsync(otherHost, author: null);
        await _records.RecordAsync(newVersion, author: null);

        Assert.Equal([otherHost, newVersion], await _records.ListAsync());
    }

    [Fact]
    public async Task RenameFolder_MovesTheEntriesOfTheType()
    {
        var save = Rover with { Type = ContentType.Save };
        await _records.RecordAsync(Rover, Maxi);
        await _records.RecordAsync(save, author: null);

        await _records.RenameFolderAsync(ContentType.Vehicle, "EXAMPLE ROVER", "Rover 2");

        Assert.Equal([Rover with { FolderName = "Rover 2" }, save], await _records.ListAsync());
        Assert.Equal(Maxi.License, (await _records.GetAuthorAsync())!.License);
    }

    [Fact]
    public async Task RenameFolder_OntoTheNameOfAnOlderEntry_KeepsOnlyTheRenamedItem()
    {
        var scout = Rover with { ListingId = "Scout", FolderName = "Scout" };
        var otherHost = Rover with { HostKey = "ownhost", HostName = "own.example" };
        var save = Rover with { Type = ContentType.Save };
        await _records.RecordAsync(scout, author: null);
        await _records.RecordAsync(Rover, author: null);
        await _records.RecordAsync(otherHost, author: null);
        await _records.RecordAsync(save, author: null);

        await _records.RenameFolderAsync(ContentType.Vehicle, "Scout", "EXAMPLE ROVER");

        Assert.Equal([scout with { FolderName = "EXAMPLE ROVER" }, save], await _records.ListAsync());
    }

    [Fact]
    public async Task RenameFolder_OfAnItemWithNoEntry_DropsTheOlderEntryOfTheNewName()
    {
        await _records.RecordAsync(Rover, author: null);

        await _records.RenameFolderAsync(ContentType.Vehicle, "Scout", "Example Rover");

        Assert.Empty(await _records.ListAsync());
    }

    [Fact]
    public async Task RenameFolder_OnlyInTheLetterCase_KeepsTheEntries()
    {
        var otherHost = Rover with { HostKey = "ownhost", HostName = "own.example" };
        await _records.RecordAsync(Rover, author: null);
        await _records.RecordAsync(otherHost, author: null);

        await _records.RenameFolderAsync(ContentType.Vehicle, "Example Rover", "EXAMPLE ROVER");

        Assert.Equal([Rover with { FolderName = "EXAMPLE ROVER" }, otherHost with { FolderName = "EXAMPLE ROVER" }], await _records.ListAsync());
    }

    [Fact]
    public async Task Writes_ThatOverlap_KeepEveryEntry()
    {
        var entries = Enumerable.Range(0, 20).Select(number => Rover with { FolderName = $"Rover {number}" }).ToList();

        await Task.WhenAll(entries.Select(entry => Task.Run(() => _records.RecordAsync(entry, author: null))));

        Assert.Equal(entries.Count, (await _records.ListAsync()).Count);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("Items = \"not a list\"\n")]
    public async Task BrokenFile_ReadsAsEmpty_StaysOnTheDisk_AndBlocksNothing(string broken)
    {
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(_paths.GetSharedContentPath(), broken);

        Assert.Empty(await _records.ListAsync());
        Assert.Null(await _records.GetAuthorAsync());
        await _records.RecordAsync(Rover, Maxi);
        await _records.RenameFolderAsync(ContentType.Vehicle, "Example Rover", "Rover 2");

        Assert.Equal(broken, await File.ReadAllTextAsync(_paths.GetSharedContentPath()));
    }

    [Fact]
    public async Task WriteThatFails_BlocksNothing()
    {
        Directory.CreateDirectory(_paths.GetSharedContentPath());

        await _records.RecordAsync(Rover, Maxi);

        Assert.Empty(await _records.ListAsync());
    }
}
