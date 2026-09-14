using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Borea.App.Views;

/// <summary>
/// The icon of a Discover category row. A tag without an icon of its own and
/// the Other row show the tag icon, because the index can add tags between releases.
/// </summary>
public static class CategoryIcon
{
    public static FuncValueConverter<string?, Geometry?> FromTag { get; } = new(tag =>
    {
        var key = tag?.ToLowerInvariant() switch
        {
            "parts" => "Icon.Cylinder",
            "celestial" => "Icon.Planet",
            "gameplay" => "Icon.Joystick",
            "user-interface" => "Icon.Cursor",
            "visual" => "Icon.Image",
            "audio" => "Icon.SpeakerLow",
            "tools" => "Icon.Eyedropper",
            _ => "Icon.Tag",
        };
        return Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null;
    });
}
