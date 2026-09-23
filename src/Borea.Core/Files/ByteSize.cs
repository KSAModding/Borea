using System.Globalization;

namespace Borea.Core.Files;

/// <summary>A byte count in decimal units, the way the download progress counts them: "38.0 MB".</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    public static string Format(long bytes, CultureInfo culture)
    {
        if (bytes < 1000)
            return bytes.ToString(culture) + " B";

        var value = bytes / 1000.0;
        var unit = 0;
        while (value >= 999.95 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return value.ToString("0.0", culture) + " " + Units[unit];
    }
}
