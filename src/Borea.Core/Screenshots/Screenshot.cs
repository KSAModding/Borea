using System.Globalization;
using System.Text.RegularExpressions;

namespace Borea.Core.Screenshots;

/// <summary>
/// A picture in a screenshots folder. Width and Height are null when the file name does not tell them.
/// </summary>
public sealed record Screenshot(string Path, DateTime TakenAt, int? Width, int? Height, long SizeBytes)
{
    private static readonly Regex GameFileName = new(@"^ksa_(?<time>\d{8}_\d{6})_(?<width>\d+)x(?<height>\d+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the time and the size from the name ksa_yyyyMMdd_HHmmss_WIDTHxHEIGHT that ScreenshotCapture.SaveToPng of the game
    /// writes in local time. A file with another name takes the last write time and no size.
    /// </summary>
    public static Screenshot FromFile(string path, DateTime lastWriteTime, long sizeBytes)
    {
        var match = GameFileName.Match(System.IO.Path.GetFileNameWithoutExtension(path));
        if (match.Success
            && DateTime.TryParseExact(match.Groups["time"].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var takenAt)
            && int.TryParse(match.Groups["width"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(match.Groups["height"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var height))
        {
            return new Screenshot(path, takenAt, width, height, sizeBytes);
        }

        return new Screenshot(path, lastWriteTime, null, null, sizeBytes);
    }
}
