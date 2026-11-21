using Borea.Core.Screenshots;

namespace Borea.Core.Tests.Screenshots;

public sealed class ScreenshotTests
{
    private static readonly DateTime LastWrite = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);

    [Theory]
    [InlineData("ksa_20261117_204530_3840x2160.png", "2026-11-17 20:45:30", 3840, 2160)]
    [InlineData("KSA_20261117_204530_1920x1080.PNG", null, null, null)]
    [InlineData("holiday.jpg", null, null, null)]
    [InlineData("ksa_20261317_204530_3840x2160.png", null, null, null)]
    [InlineData("ksa_20261117_204530_3840x.png", null, null, null)]
    [InlineData("ksa_20261117_204530_99999999999x2160.png", null, null, null)]
    public void FromFile_ReadsTheGameNameAndFallsBackForOtherNames(string fileName, string? takenAt, int? width, int? height)
    {
        var path = Path.Combine("pictures", fileName);

        var screenshot = Screenshot.FromFile(path, LastWrite, 1234);

        Assert.Equal(path, screenshot.Path);
        Assert.Equal(takenAt is null ? LastWrite : DateTime.Parse(takenAt, System.Globalization.CultureInfo.InvariantCulture), screenshot.TakenAt);
        Assert.Equal(width, screenshot.Width);
        Assert.Equal(height, screenshot.Height);
        Assert.Equal(1234, screenshot.SizeBytes);
    }
}
