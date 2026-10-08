using System.Text.Json.Nodes;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

/// <summary>
/// Only the watcher writes download.unavailable_since (RFC 0078), so an amendment keeps the mark as it is and never adds, changes or
/// removes it. The expected messages are those that tools/check_amendment.py of content-index-releases gives for the same files.
/// </summary>
public sealed class GoneReleaseAmendmentTests
{
    private const string Path = "releases/AdvancedFlightComputer/0.8.1.json";
    private const string Mark = "2026-09-23T10:24:00Z";

    private const string Immutable = "'download' changed, and identity, the version, the download and the install data never change after publish";
    private const string Rebase = "the download differs only in 'download.unavailable_since', which the watcher writes on the default branch: rebase and run tools/amend.py again";

    private static readonly string Published = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "release-files", "AdvancedFlightComputer", "0.8.1.json"));

    private static readonly IReadOnlyList<string> GameVersions = ["2026.9.22.5482"];

    [Theory]
    [InlineData(ReleaseAmender.Steward)]
    [InlineData(ReleaseAmender.Owner)]
    public void Apply_ToAReleaseWithTheMark_KeepsTheMark(ReleaseAmender amender)
    {
        var marked = ReleaseJson.Write(WithMark(Document(), Mark));
        var amendment = ReleaseAmendment.Create(new ReleaseChange { Yank = true }, GameVersions, DateTimeOffset.UtcNow);

        var amended = amendment.Apply(Path, marked, amender);

        Assert.NotNull(amended);
        Assert.Equal(marked[..^3] + ",\n  \"yanked\": true\n}\n", amended.Text);
        Assert.Equal(Mark, (string?)JsonNode.Parse(amended.Text)!["download"]!["unavailable_since"]);
        Assert.False(amended.Widens);
    }

    [Fact]
    public void Apply_ToAReleaseWithoutTheMark_AddsNoMark()
    {
        var amendment = ReleaseAmendment.Create(new ReleaseChange { Yank = true }, GameVersions, DateTimeOffset.UtcNow);

        var amended = amendment.Apply(Path, Published, ReleaseAmender.Steward);

        Assert.NotNull(amended);
        Assert.Equal(Published[..^3] + ",\n  \"yanked\": true\n}\n", amended.Text);
        Assert.False(JsonNode.Parse(amended.Text)!["download"]!.AsObject().ContainsKey("unavailable_since"));
    }

    public static TheoryData<string> Changes => ["added", "removed", "changed", "added with a yank"];

    [Theory]
    [MemberData(nameof(Changes))]
    public void Check_AChangeOfTheMark_IsRefusedForEveryAmender_AsCheckAmendmentRefusesIt(string name)
    {
        var (published, amended) = Case(name);
        var errors = new List<string>();
        var ownerOnly = new List<string>();

        ReleaseAmendmentCheck.Check(Path, published, amended, errors, ownerOnly);

        Assert.Equal([Immutable, Rebase], errors);
        Assert.Empty(ownerOnly);
    }

    [Fact]
    public void Check_TheMarkNextToAnotherChangeOfTheDownload_IsRefusedWithoutTheHintToRebase()
    {
        var amended = WithMark(Document(), Mark);
        amended["download"]!["size"] = 1;
        var errors = new List<string>();

        ReleaseAmendmentCheck.Check(Path, Document(), amended, errors, []);

        Assert.Equal([Immutable], errors);
    }

    [Fact]
    public void Check_TheMarkKeptWithAYank_Passes()
    {
        var published = WithMark(Document(), Mark);
        var amended = WithMark(Document(), Mark);
        amended["yanked"] = true;
        var errors = new List<string>();
        var ownerOnly = new List<string>();

        ReleaseAmendmentCheck.Check(Path, published, amended, errors, ownerOnly);

        Assert.Empty(errors);
        Assert.Empty(ownerOnly);
    }

    private static (JsonObject Published, JsonObject Amended) Case(string name)
    {
        switch (name)
        {
            case "added":
                return (Document(), WithMark(Document(), Mark));
            case "removed":
                return (WithMark(Document(), Mark), Document());
            case "changed":
                return (WithMark(Document(), Mark), WithMark(Document(), "2026-09-24T00:00:00Z"));
            case "added with a yank":
                var yanked = WithMark(Document(), Mark);
                yanked["yanked"] = true;
                return (Document(), yanked);
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "No such case.");
        }
    }

    private static JsonObject Document() => JsonNode.Parse(Published)!.AsObject();

    /// <summary>The mark goes last in the download, where the watcher writes it.</summary>
    private static JsonObject WithMark(JsonObject document, string since)
    {
        document["download"]!["unavailable_since"] = since;
        return document;
    }
}
