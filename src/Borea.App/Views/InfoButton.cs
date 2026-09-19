using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Borea.App.Views;

/// <summary>
/// A "?" button that explains the control next to it. The text shows as a
/// tooltip on hover and in a flyout on a click or a key press.
/// </summary>
public sealed class InfoButton : Button
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<InfoButton, string?>(nameof(Text));

    public InfoButton()
    {
        Classes.Add("info");

        var text = new TextBlock();
        text.Classes.Add("body-md");
        text.Classes.Add("info-text");
        text.Bind(TextBlock.TextProperty, this.GetObservable(TextProperty));

        var flyout = new Flyout { Content = text, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Opened += (_, _) => ToolTip.SetIsOpen(this, false);
        Flyout = flyout;

        Bind(ToolTip.TipProperty, this.GetObservable(TextProperty));
        Bind(AutomationProperties.HelpTextProperty, this.GetObservable(TextProperty));
    }

    protected override Type StyleKeyOverride => typeof(Button);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
