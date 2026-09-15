using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Core.Index;

namespace Borea.App.Tests.Views;

public sealed class MarkdownViewTests
{
    private static DescriptionImages Images(params DescriptionImage[] records) =>
        new(new MainViewModel(), new ContentImages(icon: null, records));

    private static DescriptionImage Record(string id, string url = "https://images.example/shot.png") =>
        new(id, url, new string('A', 64), 1600, 900, 400_000);

    [Fact]
    public void Split_ReferenceToARecord_PlacesThatImageWithoutLoadingIt()
    {
        var images = Images(Record("settings-window"));

        var parts = MarkdownView.Split("Before ![The settings window](ksa-image:settings-window) after", images);

        Assert.Equal(3, parts.Count);
        Assert.Equal([new MarkdownSpan(MarkdownSpanKind.Text, "Before ")], Assert.IsType<MarkdownTextPart>(parts[0]).Spans);
        var image = Assert.IsType<MarkdownImagePart>(parts[1]);
        Assert.Same(images.Find("settings-window"), image.Image);
        Assert.Equal("The settings window", image.AlternativeText);
        Assert.False(image.Image.IsLoaded);
        Assert.Equal([new MarkdownSpan(MarkdownSpanKind.Text, " after")], Assert.IsType<MarkdownTextPart>(parts[2]).Spans);
    }

    [Fact]
    public void Split_ReferenceToAnIdWithoutARecord_ShowsAMissingImageAndTheRest()
    {
        var images = Images(Record("settings-window"));

        var parts = MarkdownView.Split("![The old map](ksa-image:Settings-Window) Still shown.", images);

        Assert.Equal(new MarkdownMissingImagePart("The old map"), parts[0]);
        Assert.Equal([new MarkdownSpan(MarkdownSpanKind.Text, " Still shown.")], Assert.IsType<MarkdownTextPart>(parts[1]).Spans);
    }

    [Fact]
    public void Split_AnyOtherImage_ShowsItsAlternativeTextAndLinksStayLinks()
    {
        var images = Images(Record("shot"));

        var parts = MarkdownView.Split("""![Remote shot](https://images.example/shot.png) <img src="ksa-image:shot" alt="Html shot"> [shot](https://images.example/shot.png)![](KSA-IMAGE:shot)""", images);

        var text = Assert.IsType<MarkdownTextPart>(Assert.Single(parts));
        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Text, "Remote shot"),
                new MarkdownSpan(MarkdownSpanKind.Text, " "),
                new MarkdownSpan(MarkdownSpanKind.Text, "Html shot"),
                new MarkdownSpan(MarkdownSpanKind.Text, " "),
                new MarkdownSpan(MarkdownSpanKind.Link, "shot"),
            ],
            text.Spans);
    }

    [Fact]
    public void Split_FrozenDescription_ResolvesAgainstTheLiveListing()
    {
        var live = Images(Record("settings-window", "https://images.example/settings-window-v2.png"));

        var parts = MarkdownView.Split("![Settings](ksa-image:settings-window) ![Old map](ksa-image:map-view)", live);

        Assert.Equal("https://images.example/settings-window-v2.png", Assert.IsType<MarkdownImagePart>(parts[0]).Image.Record.Url);
        Assert.Equal(new MarkdownMissingImagePart("Old map"), parts[1]);
    }

    [Fact]
    public void Split_Changelog_ShowsEveryImageAsItsAlternativeText()
    {
        var parts = MarkdownView.Split("Fixed the ![settings window](ksa-image:settings-window).", images: null);

        var text = Assert.IsType<MarkdownTextPart>(Assert.Single(parts));
        Assert.Equal(
            [
                new MarkdownSpan(MarkdownSpanKind.Text, "Fixed the "),
                new MarkdownSpan(MarkdownSpanKind.Text, "settings window"),
                new MarkdownSpan(MarkdownSpanKind.Text, "."),
            ],
            text.Spans);
    }
}
