using Borea.Core.Index;

namespace Borea.Core.Tests.Index;

public sealed class ContentImagesTests
{
    private const string Url = "https://example.com/image.png";
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Constructor_ValidImages_CopiesTheListAndFindsDescriptionImagesById()
    {
        var icon = new IconImage(Url, Digest, 512, 512, 48213, "CC-BY-4.0", "Artwork by Example Artist", "https://example.com/original");
        var screenshot = Description("settings-window");
        var description = new List<DescriptionImage> { screenshot };

        var images = new ContentImages(icon, description);
        description.Clear();

        Assert.Same(icon, images.Icon);
        Assert.Equal(Digest.ToUpperInvariant(), icon.Sha256);
        Assert.Equal("CC-BY-4.0", icon.License);
        Assert.Equal("Artwork by Example Artist", icon.Attribution);
        Assert.Equal("https://example.com/original", icon.Source);
        Assert.Same(screenshot, Assert.Single(images.Description));
        Assert.Same(screenshot, images.FindDescriptionImage("settings-window"));
        Assert.Null(images.FindDescriptionImage("Settings-Window"));
    }

    [Fact]
    public void Constructor_NoLicense_LeavesTheOptionalFactsNull()
    {
        var image = Description("shot");

        Assert.Null(image.License);
        Assert.Null(image.Attribution);
        Assert.Null(image.Source);
    }

    [Theory]
    [InlineData("http://example.com/image.png")]
    [InlineData("example.com/image.png")]
    [InlineData("")]
    public void Constructor_UrlIsNotHttps_Throws(string url)
    {
        Assert.Throws<ArgumentException>(() => new IconImage(url, Digest, 512, 512, 1000));
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Constructor_DigestIsNot64HexCharacters_Throws(string sha256)
    {
        Assert.Throws<ArgumentException>(() => Description("shot", sha256: sha256));
    }

    [Theory]
    [InlineData(0, 100, 1000)]
    [InlineData(100, 0, 1000)]
    [InlineData(100, 100, 0)]
    public void Constructor_FactIsNotPositive_Throws(int width, int height, long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DescriptionImage("shot", Url, Digest, width, height, size));
    }

    [Fact]
    public void Constructor_EmptyOptionalFact_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DescriptionImage("shot", Url, Digest, 100, 100, 1000, license: " "));
        Assert.Throws<ArgumentException>(() => new DescriptionImage("shot", Url, Digest, 100, 100, 1000, attribution: ""));
        Assert.Throws<ArgumentException>(() => new DescriptionImage("shot", Url, Digest, 100, 100, 1000, source: "http://example.com/original"));
    }

    [Theory]
    [InlineData(IconImage.MinShorterSidePixels - 1, IconImage.MaxShorterSidePixels)]
    [InlineData(IconImage.MaxShorterSidePixels, IconImage.MinShorterSidePixels - 1)]
    [InlineData(IconImage.MaxShorterSidePixels + 1, IconImage.MaxShorterSidePixels + 1)]
    [InlineData(IconImage.MaxSideRatio * IconImage.MinShorterSidePixels + 1, IconImage.MinShorterSidePixels)]
    [InlineData(IconImage.MinShorterSidePixels, IconImage.MaxSideRatio * IconImage.MinShorterSidePixels + 1)]
    public void IconImage_PixelsOutsideTheLimits_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IconImage(Url, Digest, width, height, 1000));
    }

    [Theory]
    [InlineData(IconImage.MinShorterSidePixels, IconImage.MinShorterSidePixels)]
    [InlineData(IconImage.MaxShorterSidePixels, IconImage.MaxShorterSidePixels)]
    [InlineData(IconImage.MaxSideRatio * IconImage.MinShorterSidePixels, IconImage.MinShorterSidePixels)]
    [InlineData(IconImage.MaxShorterSidePixels, IconImage.MaxSideRatio * IconImage.MaxShorterSidePixels)]
    public void IconImage_ValuesAtTheLimits_AreValid(int width, int height)
    {
        var icon = new IconImage(Url, Digest, width, height, IconImage.MaxBytes);

        Assert.Equal((width, height), (icon.Width, icon.Height));
    }

    [Fact]
    public void IconImage_TooManyBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IconImage(Url, Digest, 512, 512, IconImage.MaxBytes + 1));
    }

    [Theory]
    [InlineData(DescriptionImage.MaxPixels + 1, 100, 1000)]
    [InlineData(100, DescriptionImage.MaxPixels + 1, 1000)]
    [InlineData(100, 100, DescriptionImage.MaxBytes + 1)]
    public void DescriptionImage_OutsideTheLimits_Throws(int width, int height, long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DescriptionImage("shot", Url, Digest, width, height, size));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-shot")]
    [InlineData("shot_")]
    [InlineData("settings window")]
    [InlineData("settings.window")]
    public void DescriptionImage_InvalidId_Throws(string id)
    {
        Assert.Throws<ArgumentException>(() => Description(id));
    }

    [Fact]
    public void DescriptionImage_IdLength_IsLimited()
    {
        Assert.Equal(64, Description(new string('a', 64)).Id.Length);
        Assert.Throws<ArgumentException>(() => Description(new string('a', 65)));
    }

    [Fact]
    public void Constructor_DuplicateDescriptionId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ContentImages(null, new[] { Description("shot"), Description("shot") }));
    }

    [Fact]
    public void Constructor_IdsThatDifferOnlyInCase_AreDistinct()
    {
        var images = new ContentImages(null, new[] { Description("shot"), Description("Shot") });

        Assert.Equal(2, images.Description.Count);
    }

    [Fact]
    public void Constructor_MoreThanTheMaximumDescriptionImages_Throws()
    {
        var description = Enumerable.Range(0, ContentImages.MaxDescriptionImages + 1)
            .Select(index => Description($"shot-{index}"))
            .ToArray();

        Assert.Throws<ArgumentException>(() => new ContentImages(null, description));
    }

    private static DescriptionImage Description(string id, string sha256 = Digest) =>
        new(id, Url, sha256, 1600, 900, 402117);
}
