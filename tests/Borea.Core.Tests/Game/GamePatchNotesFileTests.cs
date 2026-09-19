using System.Text;
using Borea.Core.Game;

namespace Borea.Core.Tests.Game;

public sealed class GamePatchNotesFileTests
{
    [Fact]
    public void NameOf_UsesYearMonthAndRevision()
    {
        Assert.Equal("v2026.9.X.5438.json", GamePatchNotesFile.NameOf(GameVersion.Parse("2026.9.10.5438")));
    }

    [Fact]
    public void Parse_FileWithAByteOrderMark_ReadsTheRange()
    {
        var json = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{ "build": "2026.9.10.5438", "fromRevision": 5402, "toRevision": 5438, "commits": [ { "rev": 5403, "lines": ["Fixed the Milky Way."] } ] }""")).ToArray();

        var notes = GamePatchNotesFile.Parse(json);

        Assert.NotNull(notes);
        Assert.Equal(5402, notes.FromRevision);
        Assert.Equal(5438, notes.Revision);
        Assert.Equal(["Fixed the Milky Way."], notes.Lines);
    }

    [Theory]
    [InlineData("<html>Not a version file</html>")]
    [InlineData("""{ "build": "2026.9.10.5438", "commits": """)]
    [InlineData("""{ "toRevision": 5438, "commits": [] }""")]
    public void Parse_NotAVersionFile_IsNull(string text)
    {
        Assert.Null(GamePatchNotesFile.Parse(Encoding.UTF8.GetBytes(text)));
    }
}
