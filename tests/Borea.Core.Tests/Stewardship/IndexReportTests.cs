using Borea.Core.Mods;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class IndexReportTests
{
    private static readonly Uri Url = new("https://github.com/KSAModding/content-index/issues/12");
    private static readonly DateTimeOffset Created = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A takedown body as GitHub writes it from .github/ISSUE_TEMPLATE/takedown.yml.</summary>
    internal const string TakedownBody = "### Listing id\n\nMeasureTools\n\n### Ground\n\nThe content breaks the law or the license of what it contains\n\n### What is wrong\n\nThe archive ships the textures of another mod.\nSee https://example.com/license.\n\n### Who you are\n\nI made the textures.";

    /// <summary>An id dispute body as GitHub writes it from .github/ISSUE_TEMPLATE/id-dispute.yml, with the optional field left empty.</summary>
    internal const string DisputeBody = "### Listing id\n\nMeasureTools\n\n### What is disputed\n\nThe id is the folder name of my content, and somebody else listed it\n\n### Your forums thread\n\nhttps://forums.ahwoo.com/threads/measure-tools.123/\n\n### Your claim\n\nI announced it first.\n\n### The other party\n\n_No response_";

    [Fact]
    public void FromIssue_Takedown_ReadsEachSectionOfTheForm()
    {
        var report = IndexReport.FromIssue(12, Url, "[Takedown] MeasureTools uses my textures", "alice", Created, TakedownBody)!;

        Assert.Equal((12, Url, "[Takedown] MeasureTools uses my textures", "alice", Created, IndexReportKind.Takedown), (report.Number, report.Url, report.Title, report.Author, report.Created, report.Kind));
        Assert.Equal(("MeasureTools", "MeasureTools", (string?)null), (report.ListingText, report.Id, report.Version));
        Assert.Equal("The content breaks the law or the license of what it contains", report.Ground);
        Assert.Equal("The archive ships the textures of another mod.\nSee https://example.com/license.", report.WhatIsWrong);
        Assert.Equal("I made the textures.", report.Reporter);
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null), (report.Disputed, report.ForumsThread, report.Claim, report.OtherParty));
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void FromIssue_LeaveAPack_NamesThePackAndTheModsThatWhatIsWrongMentions()
    {
        const string body = "### Listing id\n\nmy-pack\n\n### Ground\n\nI am the author of a mod and want it out of this pack\n\n### What is wrong\n\nPlease take `MyMod` out of the pack. Other.Mod is fine, and so is mymod.\n\n### Who you are\n\nThe author of MyMod.";

        var report = IndexReport.FromIssue(12, Url, "[Takedown] my-pack", "alice", Created, body, authorId: 5)!;

        Assert.True(report.IsLeavePack);
        Assert.Equal(("my-pack", 5L), (report.Id, report.AuthorId));
        Assert.Equal(["MyMod", "Other.Mod"], report.MentionedIds(["MyMod", "Other.Mod", "Other", "my-pack"]));
        Assert.Empty(report.MentionedIds(["Mod", "Unknown"]));
    }

    [Fact]
    public void IsLeavePack_OnlyTheGroundOfTheTakedownForm()
    {
        var other = IndexReport.FromIssue(12, Url, "[Takedown] MeasureTools", "alice", Created, TakedownBody)!;
        var dispute = IndexReport.FromIssue(13, Url, "[Dispute] my-pack", "bob", Created, DisputeBody.Replace("The id is the folder name", IndexReport.LeavePackGround, StringComparison.Ordinal))!;

        Assert.False(other.IsLeavePack);
        Assert.False(dispute.IsLeavePack);
        Assert.Null(other.AuthorId);
    }

    [Fact]
    public void FromIssue_Dispute_ReadsEachSectionOfTheForm_AndAnEmptyOptionalFieldIsNull()
    {
        var report = IndexReport.FromIssue(13, Url, "[Dispute] MeasureTools is mine", "bob", Created, DisputeBody.Replace("\n", "\r\n", StringComparison.Ordinal))!;

        Assert.Equal(IndexReportKind.Dispute, report.Kind);
        Assert.Equal("MeasureTools", report.Id);
        Assert.Equal("The id is the folder name of my content, and somebody else listed it", report.Disputed);
        Assert.Equal("https://forums.ahwoo.com/threads/measure-tools.123/", report.ForumsThread);
        Assert.Equal(new Uri("https://forums.ahwoo.com/threads/measure-tools.123/"), report.ForumsUrl);
        Assert.Equal("I announced it first.", report.Claim);
        Assert.Null(report.OtherParty);
        Assert.Equal(((string?)null, (string?)null, (string?)null), (report.Ground, report.WhatIsWrong, report.Reporter));
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void FromIssue_BodyEditedByHand_NamesTheSectionsItLacks()
    {
        const string body = "### Listing id\n\nMeasureTools\n\n### What is wrong\n\nI removed the ground and my name.";

        var report = IndexReport.FromIssue(12, Url, "[Takedown] MeasureTools", "alice", Created, body)!;

        Assert.Equal([IndexReport.GroundLabel, IndexReport.ReporterLabel], report.Missing);
        Assert.Equal("MeasureTools", report.Id);
        Assert.Equal("I removed the ground and my name.", report.WhatIsWrong);
        Assert.Null(report.Ground);
    }

    [Fact]
    public void FromIssue_DisputeWithoutTheListingId_HasNoId_AndNamesTheSection()
    {
        const string body = "### What is disputed\n\nSomething else, explained below\n\n### Your forums thread\n\nnot a link\n\n### Your claim\n\nIt is mine.";

        var report = IndexReport.FromIssue(13, Url, "[Dispute] ", "bob", Created, body)!;

        Assert.Equal([IndexReport.ListingLabel], report.Missing);
        Assert.Equal(((string?)null, (string?)null), (report.ListingText, report.Id));
        Assert.Equal("not a link", report.ForumsThread);
        Assert.Null(report.ForumsUrl);
    }

    [Theory]
    [InlineData("https://forums.ahwoo.com/threads/measure-tools.123/", true)]
    [InlineData("https://forums.ahwoo.com/threads/measure-tools.123/ (my old thread)", false)]
    [InlineData("https://forums.ahwoo.com/threads/measure-tools.123/\nhttps://example.com/", false)]
    [InlineData("https://example.com/threads/measure-tools.123/", false)]
    [InlineData("http://forums.ahwoo.com/threads/measure-tools.123/", false)]
    public void ForumsUrl_OpensOnlyAThreadLinkOfTheForums(string thread, bool opens)
    {
        var body = $"### Listing id\n\nMeasureTools\n\n### What is disputed\n\nThe id\n\n### Your forums thread\n\n{thread}\n\n### Your claim\n\nIt is mine.";

        var report = IndexReport.FromIssue(13, Url, "[Dispute] MeasureTools", "bob", Created, body)!;

        Assert.Equal(thread, report.ForumsThread);
        Assert.Equal(opens ? new Uri(thread) : null, report.ForumsUrl);
    }

    [Fact]
    public void FromIssue_RepeatedHeadingInsideAnAnswer_StaysTextOfThatAnswer()
    {
        const string body = "### Listing id\n\nMeasureTools\n\n### Ground\n\nSomething else, explained below\n\n### What is wrong\n\nThe readme says:\n### Listing id\nOther\n\n### Who you are\n\nThe author.";

        var report = IndexReport.FromIssue(12, Url, "[Takedown] MeasureTools", "alice", Created, body)!;

        Assert.Equal("MeasureTools", report.Id);
        Assert.Equal("The readme says:\n### Listing id\nOther", report.WhatIsWrong);
        Assert.Equal("The author.", report.Reporter);
    }

    [Theory]
    [InlineData("[takedown]MeasureTools", IndexReportKind.Takedown)]
    [InlineData("  [Dispute] MeasureTools", IndexReportKind.Dispute)]
    [InlineData("Takedown of MeasureTools", null)]
    [InlineData("Mention the owner [Takedown]", null)]
    [InlineData("", null)]
    public void KindOf_TakesTheTagOfTheForm(string title, IndexReportKind? kind)
    {
        Assert.Equal(kind, IndexReport.KindOf(title));
    }

    [Fact]
    public void FromIssue_TitleWithoutTag_IsNoReport()
    {
        Assert.Null(IndexReport.FromIssue(14, Url, "Mention the owner when somebody else changes a listing", "alice", Created, TakedownBody));
    }

    [Theory]
    [InlineData("my-pack 1.2.0", "my-pack", "1.2.0")]
    [InlineData("my-pack@1.2.0", "my-pack", "1.2.0")]
    [InlineData("`my-pack` v1.2.0+build.5", "my-pack", "1.2.0+build.5")]
    [InlineData("  My.Mod  ", "My.Mod", null)]
    [InlineData("my-pack 1.2", null, null)]
    [InlineData("the pack my-pack", null, null)]
    [InlineData("listings/MyMod.toml", null, null)]
    [InlineData("CON", null, null)]
    [InlineData("", null, null)]
    public void IdOf_ReadsAnIdAndAnOptionalPackVersion(string text, string? id, string? version)
    {
        Assert.Equal((id, version), IndexReport.IdOf(text));
    }

    [Fact]
    public void FormUrl_OpensTheFormWithTheListingIdFilledIn()
    {
        Assert.Equal(
            "https://github.com/KSAModding/content-index/issues/new?template=takedown.yml&listing=MeasureTools",
            IndexReport.FormUrl(IndexReportKind.Takedown, "MeasureTools").AbsoluteUri);
        Assert.Equal(
            "https://github.com/KSAModding/content-index/issues/new?template=id-dispute.yml&listing=tools-pack",
            IndexReport.FormUrl(IndexReportKind.Dispute, "tools-pack").AbsoluteUri);
    }

    [Fact]
    public void FormUrl_TakedownOfOnePackVersion_NamesTheVersionTheWayTheStewardPageReadsIt()
    {
        var url = IndexReport.FormUrl(IndexReportKind.Takedown, "tools-pack", ModVersion.Parse("1.1.0-beta.1"));

        Assert.Equal("https://github.com/KSAModding/content-index/issues/new?template=takedown.yml&listing=tools-pack%201.1.0-beta.1", url.AbsoluteUri);
        var listing = Uri.UnescapeDataString(url.Query.Split("&listing=")[1]);
        Assert.Equal(("tools-pack", "1.1.0-beta.1"), IndexReport.IdOf(listing));
    }

    [Fact]
    public void FormUrl_RefusesAnIdThatIsNoListingId_AndADisputeOfOneVersion()
    {
        Assert.Throws<ArgumentException>(() => IndexReport.FormUrl(IndexReportKind.Takedown, "my mod&template=x"));
        Assert.Throws<ArgumentException>(() => IndexReport.FormUrl(IndexReportKind.Dispute, "tools-pack", ModVersion.Parse("1.1.0")));
    }

    [Fact]
    public void FromIssue_PackIdWithAVersion_NamesTheVersion()
    {
        var report = IndexReport.FromIssue(12, Url, "[Takedown] One pack version", "alice", Created, TakedownBody.Replace("\n\nMeasureTools\n\n", "\n\ntools-pack 1.1.0\n\n", StringComparison.Ordinal))!;

        Assert.Equal(("tools-pack 1.1.0", "tools-pack", "1.1.0"), (report.ListingText, report.Id, report.Version));
    }

    [Fact]
    public void Change_FromAReport_DelistAndRetractCloseIt_ADisputeOnlyNamesIt()
    {
        var delist = IndexStatusChange.Delist("MyMod", "Taken down.") with { Report = 12 };
        var retract = IndexStatusChange.Retract("my-pack", "1.0.0", "Broken.") with { Report = 13 };
        var dispute = IndexStatusChange.Dispute("MyMod", "Contested.") with { Report = 14 };

        Assert.Equal("Sets `delisted` on `MyMod` in `index-status.toml`.\n\nReason: Taken down.\n\nCloses #12\n\n@alice owns `MyMod`.", delist.Body(["alice"]));
        Assert.Equal("Sets `retracted` on version 1.0.0 of `my-pack` in `index-status.toml`.\n\nReason: Broken.\n\nCloses #13", retract.Body([]));
        Assert.Equal("Sets `disputed` on `MyMod` in `index-status.toml`.\n\nReason: Contested.\n\nFor report #14.\n\n@alice owns `MyMod`.", dispute.Body(["alice"]));
        Assert.Equal((true, true, false), (delist.ClosesReport, retract.ClosesReport, dispute.ClosesReport));
        Assert.Equal("steward/delisted-mymod", delist.Branch);
    }

    [Fact]
    public void Change_WithoutAReport_NamesNoReport()
    {
        var delist = IndexStatusChange.Delist("MyMod", "Taken down.");

        Assert.Equal("Sets `delisted` on `MyMod` in `index-status.toml`.\n\nReason: Taken down.", delist.Body([]));
        Assert.False(delist.ClosesReport);
    }
}
