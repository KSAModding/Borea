using System.Globalization;

namespace Borea.App.Formatting;

public sealed class RegionalFormatOption
{
    public string? CultureName { get; }

    public string DisplayName { get; }

    internal CultureInfo Culture { get; }

    internal RegionalFormatOption(string? cultureName, string displayName, CultureInfo culture)
    {
        CultureName = cultureName;
        DisplayName = displayName;
        Culture = culture;
    }

    public override string ToString() => DisplayName;
}
