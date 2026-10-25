using Borea.Core.Sharing;

namespace Borea.Core.Tests.Sharing;

public sealed class ContentIdsTests
{
    [Fact]
    public void Add_KeepsTheFirstSpellingOfEachIdPerKind()
    {
        var ids = new ContentIds();

        Assert.True(ids.Add(ContentIdKind.Part, "RoverWheel"));
        Assert.False(ids.Add(ContentIdKind.Part, "roverwheel"));
        Assert.True(ids.Add(ContentIdKind.Character, "RoverWheel"));
        Assert.False(ids.Add(ContentIdKind.Part, " "));
        Assert.False(ids.Add(ContentIdKind.Part, null));

        Assert.Equal(["RoverWheel"], ids.Of(ContentIdKind.Part));
        Assert.Equal(2, ids.Count);
        Assert.True(ids.Contains(new ContentId(ContentIdKind.Part, "ROVERWHEEL")));
    }
}
