using Borea.Storage.Announcements;

namespace Borea.Storage.Tests.Announcements;

public sealed class AnnouncementReaderTests
{
    private const string ValidPost = """
        [[posts]]
        id = "index-100"
        title = "100 mods"
        date = 2026-09-10
        body = "The index lists **100 mods**."
        """;

    [Fact]
    public async Task ReadAsync_TheCommittedFile_HasNoPosts()
    {
        var file = await new AnnouncementReader().ReadAsync(Path.Combine(AppContext.BaseDirectory, "Announcements", "announcements.toml"));

        Assert.Empty(file.Posts);
        Assert.Empty(file.SkippedPosts);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_HasNoPosts()
    {
        var file = await new AnnouncementReader().ReadAsync(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid(), "announcements.toml"));

        Assert.Empty(file.Posts);
    }

    [Fact]
    public void Parse_ValidPosts_ReadsEveryField()
    {
        var file = AnnouncementReader.Parse($$"""
            spec_version = 1
            {{ValidPost}}

            [[posts]]
            id = "Testers.Wanted"
            title = "  Testers wanted  "
            date = 2026-09-12T18:30:00+02:00
            body = "Try the beta."
            link = "https://github.com/KSAModding/Borea/releases"
            """);

        Assert.Empty(file.SkippedPosts);
        Assert.Equal(2, file.Posts.Count);
        var first = file.Posts[0];
        Assert.Equal("index-100", first.Id);
        Assert.Equal("100 mods", first.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), first.Date);
        Assert.Equal("The index lists **100 mods**.", first.Body);
        Assert.Null(first.Link);
        var second = file.Posts[1];
        Assert.Equal("Testers wanted", second.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 16, 30, 0, TimeSpan.Zero), second.Date);
        Assert.Equal("https://github.com/KSAModding/Borea/releases", second.Link);
    }

    [Fact]
    public void Parse_EmptyPostsArray_HasNoPosts()
    {
        var file = AnnouncementReader.Parse("spec_version = 1\nposts = []");

        Assert.Empty(file.Posts);
    }

    [Theory]
    [InlineData("title = \"T\"\ndate = 2026-09-10\nbody = \"B\"")]
    [InlineData("id = \"CON\"\ntitle = \"T\"\ndate = 2026-09-10\nbody = \"B\"")]
    [InlineData("id = \"bad id\"\ntitle = \"T\"\ndate = 2026-09-10\nbody = \"B\"")]
    [InlineData("id = \"a\"\ntitle = \" \"\ndate = 2026-09-10\nbody = \"B\"")]
    [InlineData("id = \"a\"\ntitle = \"T\"\nbody = \"B\"")]
    [InlineData("id = \"a\"\ntitle = \"T\"\ndate = \"2026-09-10\"\nbody = \"B\"")]
    [InlineData("id = \"a\"\ntitle = \"T\"\ndate = 2026-09-10T12:00:00\nbody = \"B\"")]
    [InlineData("id = \"a\"\ntitle = \"T\"\ndate = 2026-09-10")]
    [InlineData("id = \"a\"\ntitle = \"T\"\ndate = 2026-09-10\nbody = \"B\"\nlink = \"http://example.com\"")]
    [InlineData("id = \"a\"\ntitle = \"T\"\ndate = 2026-09-10\nbody = \"B\"\nlink = \"not a url\"")]
    [InlineData("id = \"a\"\ntitle = \"T\"\ndate = 2026-09-10\nbody = \"B\"\nlink = 5")]
    public void Parse_BrokenPost_IsSkippedAndTheOthersRead(string broken)
    {
        var file = AnnouncementReader.Parse($"spec_version = 1\n[[posts]]\n{broken}\n\n{ValidPost}");

        Assert.Equal("index-100", Assert.Single(file.Posts).Id);
        Assert.StartsWith("Post 1: ", Assert.Single(file.SkippedPosts), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateId_KeepsTheFirstPost()
    {
        var file = AnnouncementReader.Parse($"spec_version = 1\n{ValidPost}\n\n{ValidPost.Replace("100 mods\"", "Again\"", StringComparison.Ordinal).Replace("index-100", "INDEX-100", StringComparison.Ordinal)}");

        Assert.Equal("100 mods", Assert.Single(file.Posts).Title);
        Assert.Contains("more than once", Assert.Single(file.SkippedPosts), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("spec_version = ")]
    [InlineData("title = \"no version\"")]
    [InlineData("spec_version = \"1\"")]
    [InlineData("spec_version = 2")]
    [InlineData("spec_version = 1\nposts = \"none\"")]
    public void Parse_BrokenFile_Throws(string text)
    {
        Assert.Throws<InvalidDataException>(() => AnnouncementReader.Parse(text));
    }
}
