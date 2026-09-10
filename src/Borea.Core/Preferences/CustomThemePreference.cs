namespace Borea.Core.Preferences;

public sealed class CustomThemePreference
{
    public string Name { get; }

    public string MainColor { get; }

    public string SecondaryColor { get; }

    public string GlobalPanelsColor { get; }

    public string TextColor { get; }

    public CustomThemePreference(string name, string mainColor, string secondaryColor, string globalPanelsColor, string textColor)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Theme name cannot be null or whitespace.", nameof(name));

        Name = name;
        MainColor = ValidateColor(mainColor, nameof(mainColor));
        SecondaryColor = ValidateColor(secondaryColor, nameof(secondaryColor));
        GlobalPanelsColor = ValidateColor(globalPanelsColor, nameof(globalPanelsColor));
        TextColor = ValidateColor(textColor, nameof(textColor));
    }

    private static string ValidateColor(string color, string paramName)
    {
        if (color is null)
            throw new ArgumentNullException(paramName);

        if (color.Length != 7 || color[0] != '#' || !color.AsSpan(1).ContainsOnlyAsciiHexDigits())
            throw new ArgumentException("Theme colors must use the #RRGGBB format.", paramName);

        return color;
    }
}

internal static class HexColorExtensions
{
    public static bool ContainsOnlyAsciiHexDigits(this ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
                return false;
        }

        return true;
    }
}
