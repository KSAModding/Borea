using System.Globalization;

namespace Borea.App.Localization;

public sealed class SupportedCulture
{
    public string Name => Culture.Name;

    public string DisplayName { get; }

    internal CultureInfo Culture { get; }

    internal SupportedCulture(string name, string displayName)
    {
        Culture = CultureInfo.GetCultureInfo(name);
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
