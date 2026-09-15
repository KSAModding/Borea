using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Borea.App.Views;

/// <summary>
/// The icon of a link in the detail panel, by the key the listing gives the
/// link. A key the design in #8 does not draw shows the plain link icon.
/// </summary>
public static class LinkIcon
{
    public static FuncValueConverter<string?, Geometry?> FromKey { get; } = new(key =>
    {
        var resource = key?.ToLowerInvariant() switch
        {
            "forums" => "Icon.Chats",
            "repository" => "Icon.GithubLogo",
            "spacedock" => "Icon.Lightning",
            "bugtracker" => "Icon.Bug",
            "discussions" => "Icon.ChatCircle",
            _ => "Icon.LinkSimple",
        };
        return Application.Current?.TryFindResource(resource, out var value) == true ? value as Geometry : null;
    });
}
