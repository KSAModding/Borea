using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

public partial class InstallChoices : UserControl
{
    public static readonly StyledProperty<bool> ShowsBlockedTextProperty =
        AvaloniaProperty.Register<InstallChoices, bool>(nameof(ShowsBlockedText), defaultValue: true);

    /// <summary>False for a host that keeps this line in view on its own, because its choices scroll.</summary>
    public bool ShowsBlockedText
    {
        get => GetValue(ShowsBlockedTextProperty);
        set => SetValue(ShowsBlockedTextProperty, value);
    }

    public InstallChoices()
    {
        InitializeComponent();
    }
}
