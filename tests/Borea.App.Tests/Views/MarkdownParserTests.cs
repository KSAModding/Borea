using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class MarkdownParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n \n")]
    public void Parse_NoText_ReturnsNoBlocks(string? markdown)
    {
        Assert.Empty(MarkdownParser.Parse(markdown));
    }

    [Fact]
    public void Parse_JoinsParagraphLinesAndSplitsOnBlankLines()
    {
        var blocks = MarkdownParser.Parse("First line\r\nsecond line\n\nNext paragraph");

        Assert.Equal(
            [
                new MarkdownBlock(MarkdownBlockKind.Paragraph, "First line second line"),
                new MarkdownBlock(MarkdownBlockKind.Paragraph, "Next paragraph"),
            ],
            blocks);
    }

    [Fact]
    public void Parse_HeadingsKeepTheirLevel()
    {
        var blocks = MarkdownParser.Parse("# One\ntext\n### Three");

        Assert.Equal(
            [
                new MarkdownBlock(MarkdownBlockKind.Heading, "One", 1),
                new MarkdownBlock(MarkdownBlockKind.Paragraph, "text"),
                new MarkdownBlock(MarkdownBlockKind.Heading, "Three", 3),
            ],
            blocks);
    }

    [Fact]
    public void Parse_BulletsAndNumberedItems()
    {
        var blocks = MarkdownParser.Parse("- dash\n* star\n  + nested plus\n2. second");

        Assert.Equal(
            [
                new MarkdownBlock(MarkdownBlockKind.ListItem, "dash", Marker: "•"),
                new MarkdownBlock(MarkdownBlockKind.ListItem, "star", Marker: "•"),
                new MarkdownBlock(MarkdownBlockKind.ListItem, "nested plus", Marker: "•"),
                new MarkdownBlock(MarkdownBlockKind.ListItem, "second", Marker: "2."),
            ],
            blocks);
    }

    [Fact]
    public void Parse_FencedCodeKeepsItsLinesAsWritten()
    {
        var blocks = MarkdownParser.Parse("Before\n```json\n{ \"a\": 1 }\n  # not a heading\n```\nAfter");

        Assert.Equal(
            [
                new MarkdownBlock(MarkdownBlockKind.Paragraph, "Before"),
                new MarkdownBlock(MarkdownBlockKind.Code, "{ \"a\": 1 }\n  # not a heading"),
                new MarkdownBlock(MarkdownBlockKind.Paragraph, "After"),
            ],
            blocks);
    }

    [Fact]
    public void Parse_UnclosedFence_RunsToTheEnd()
    {
        var block = Assert.Single(MarkdownParser.Parse("```\nline one\nline two"));

        Assert.Equal(new MarkdownBlock(MarkdownBlockKind.Code, "line one\nline two"), block);
    }

    [Fact]
    public void ParseInline_SplitsEveryKindOfSpan()
    {
        var spans = MarkdownParser.ParseInline("Run **bold** and __strong__, *it* _em_ `code` [label](https://example.com) end");

        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Text, "Run "),
                new MarkdownSpan(MarkdownSpanKind.Bold, "bold"),
                new MarkdownSpan(MarkdownSpanKind.Text, " and "),
                new MarkdownSpan(MarkdownSpanKind.Bold, "strong"),
                new MarkdownSpan(MarkdownSpanKind.Text, ", "),
                new MarkdownSpan(MarkdownSpanKind.Italic, "it"),
                new MarkdownSpan(MarkdownSpanKind.Text, " "),
                new MarkdownSpan(MarkdownSpanKind.Italic, "em"),
                new MarkdownSpan(MarkdownSpanKind.Text, " "),
                new MarkdownSpan(MarkdownSpanKind.Code, "code"),
                new MarkdownSpan(MarkdownSpanKind.Text, " "),
                new MarkdownSpan(MarkdownSpanKind.Link, "label"),
                new MarkdownSpan(MarkdownSpanKind.Text, " end"),
            ],
            spans);
    }

    [Fact]
    public void ParseInline_PlainText_IsOneSpan()
    {
        Assert.Equal([new MarkdownSpan(MarkdownSpanKind.Text, "just text")], MarkdownParser.ParseInline("just text"));
        Assert.Empty(MarkdownParser.ParseInline(""));
    }
}
