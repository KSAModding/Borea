using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Borea.App.Views;

/// <summary>
/// Keeps a tooltip that only repeats the text of its TextBlock closed while the
/// whole text shows, so it opens only when the block cut the text off.
/// </summary>
public static class RepeatedToolTip
{
    public static void Register() => ToolTip.ToolTipOpeningEvent.AddClassHandler<TextBlock>(OnOpening);

    internal static bool IsCut(TextBlock block) => block.TextLayout.TextLines.Any(line => line.HasCollapsed);

    private static void OnOpening(TextBlock block, CancelRoutedEventArgs args)
    {
        if (ToolTip.GetTip(block) is string tip && tip == block.Text && !IsCut(block))
            args.Cancel = true;
    }
}
