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

    [Fact]
    public void ParseInline_Image_KeepsItsAlternativeTextAndDestination()
    {
        var spans = MarkdownParser.ParseInline("""See ![The settings window](ksa-image:settings-window "Settings") and ![Map](<ksa-image:map_view>).""");

        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Text, "See "),
                new MarkdownSpan(MarkdownSpanKind.Image, "The settings window", "ksa-image:settings-window"),
                new MarkdownSpan(MarkdownSpanKind.Text, " and "),
                new MarkdownSpan(MarkdownSpanKind.Image, "Map", "ksa-image:map_view"),
                new MarkdownSpan(MarkdownSpanKind.Text, "."),
            ],
            spans);
    }

    [Fact]
    public void ParseInline_LinkedImage_IsTheImage()
    {
        Assert.Equal(
            [new MarkdownSpan(MarkdownSpanKind.Image, "Logo", "ksa-image:logo")],
            MarkdownParser.ParseInline("[![Logo](ksa-image:logo)](https://example.com)"));
    }

    [Fact]
    public void ParseInline_LinkToAnImageFile_StaysALink()
    {
        Assert.Equal(
            [new MarkdownSpan(MarkdownSpanKind.Link, "screenshot")],
            MarkdownParser.ParseInline("[screenshot](https://example.com/shot.png)"));
    }

    [Fact]
    public void ParseInline_ImageInRawHtml_BecomesItsAlternativeText()
    {
        var spans = MarkdownParser.ParseInline("""<img src="https://example.com/a.png" alt="Tom &amp; Jerry"> and <IMG src=x> **<img alt="Map">**""");

        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Text, "Tom & Jerry"),
                new MarkdownSpan(MarkdownSpanKind.Text, " and "),
                new MarkdownSpan(MarkdownSpanKind.Text, " "),
                new MarkdownSpan(MarkdownSpanKind.Bold, "Map"),
            ],
            spans);
    }

    [Fact]
    public void ParseInline_ImageInsideBold_KeepsTheBoldTextAndTheImage()
    {
        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Bold, "Screenshot "),
                new MarkdownSpan(MarkdownSpanKind.Image, "Map", "ksa-image:map"),
            ],
            MarkdownParser.ParseInline("**Screenshot ![Map](ksa-image:map)**"));
    }

    [Fact]
    public void ParseInline_ImageAloneInsideItalic_IsTheImage()
    {
        Assert.Equal([new MarkdownSpan(MarkdownSpanKind.Image, "Map", "ksa-image:map")], MarkdownParser.ParseInline("*![Map](ksa-image:map)*"));
        Assert.Equal([new MarkdownSpan(MarkdownSpanKind.Image, "Map", "ksa-image:map_view")], MarkdownParser.ParseInline("_![Map](ksa-image:map_view)_"));
    }

    [Fact]
    public void ParseInline_ImageInsideALinkLabel_KeepsTheLinkTextAndTheImage()
    {
        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Link, "see "),
                new MarkdownSpan(MarkdownSpanKind.Image, "Map", "ksa-image:map"),
            ],
            MarkdownParser.ParseInline("[see ![Map](ksa-image:map)](https://example.com)"));
    }

    [Fact]
    public void ParseInline_UnderscoresInsideWords_AreNotEmphasis()
    {
        var spans = MarkdownParser.ParseInline("Set snake_case in the file_name. ![Map](ksa-image:map_view)");

        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Text, "Set snake_case in the file_name. "),
                new MarkdownSpan(MarkdownSpanKind.Image, "Map", "ksa-image:map_view"),
            ],
            spans);
    }

    [Fact]
    public void Parse_ReferenceStyleImages_UseTheirDefinitionsAndHideThem()
    {
        var blocks = MarkdownParser.Parse("![Map][map] and ![Shot][]\n\n[map]: ksa-image:map-view\n[Shot]: <ksa-image:shot> \"Title\"\n\n```\n[code]: stays\n```");

        Assert.Equal(
            [
                new MarkdownBlock(MarkdownBlockKind.Paragraph, "![Map](<ksa-image:map-view>) and ![Shot](<ksa-image:shot>)"),
                new MarkdownBlock(MarkdownBlockKind.Code, "[code]: stays"),
            ],
            blocks);
    }

    [Fact]
    public void Parse_ReferenceStyleLinkedImage_ResolvesTheImageAndTheLinkAroundIt()
    {
        var block = Assert.Single(MarkdownParser.Parse("[![Logo][logo]][site]\n\n[logo]: ksa-image:logo\n[site]: https://example.com"));

        Assert.Equal("[![Logo](<ksa-image:logo>)](<https://example.com>)", block.Text);
        Assert.Equal([new MarkdownSpan(MarkdownSpanKind.Image, "Logo", "ksa-image:logo")], MarkdownParser.ParseInline(block.Text));
    }
}
