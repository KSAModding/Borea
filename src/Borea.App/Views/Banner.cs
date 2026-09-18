using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Borea.App.Views;

public enum BannerKind
{
    Neutral,
    Accent,
    Outline,
    Error,
    Success,
}

/// <summary>
/// A message across the page body, from the banner element of the design. The
/// title, the action and the close button show only when they are set, and the
/// kind picks the colors.
/// </summary>
public sealed class Banner : TemplatedControl
{
    public static readonly StyledProperty<BannerKind> KindProperty =
        AvaloniaProperty.Register<Banner, BannerKind>(nameof(Kind));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(Message));

    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(ActionText));

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<Banner, ICommand?>(nameof(ActionCommand));

    public static readonly StyledProperty<object?> ActionCommandParameterProperty =
        AvaloniaProperty.Register<Banner, object?>(nameof(ActionCommandParameter));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<Banner, ICommand?>(nameof(DismissCommand));

    /// <summary>The accessible name of the close button.</summary>
    public static readonly StyledProperty<string?> DismissTextProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(DismissText));

    public static readonly StyledProperty<IBrush?> TitleForegroundProperty =
        AvaloniaProperty.Register<Banner, IBrush?>(nameof(TitleForeground));

    public static readonly StyledProperty<IBrush?> ActionForegroundProperty =
        AvaloniaProperty.Register<Banner, IBrush?>(nameof(ActionForeground));

    public BannerKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public object? ActionCommandParameter
    {
        get => GetValue(ActionCommandParameterProperty);
        set => SetValue(ActionCommandParameterProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public string? DismissText
    {
        get => GetValue(DismissTextProperty);
        set => SetValue(DismissTextProperty, value);
    }

    public IBrush? TitleForeground
    {
        get => GetValue(TitleForegroundProperty);
        set => SetValue(TitleForegroundProperty, value);
    }

    public IBrush? ActionForeground
    {
        get => GetValue(ActionForegroundProperty);
        set => SetValue(ActionForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != KindProperty)
            return;

        var kind = change.GetNewValue<BannerKind>();
        PseudoClasses.Set(":accent", kind == BannerKind.Accent);
        PseudoClasses.Set(":outline", kind == BannerKind.Outline);
        PseudoClasses.Set(":error", kind == BannerKind.Error);
        PseudoClasses.Set(":success", kind == BannerKind.Success);
    }
}
