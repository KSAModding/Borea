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

    /// <summary>The action in front of <see cref="ActionText"/>. It shows only when it has text.</summary>
    public static readonly StyledProperty<string?> SecondaryActionTextProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(SecondaryActionText));

    public static readonly StyledProperty<ICommand?> SecondaryActionCommandProperty =
        AvaloniaProperty.Register<Banner, ICommand?>(nameof(SecondaryActionCommand));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<Banner, ICommand?>(nameof(DismissCommand));

    /// <summary>The accessible name of the close button.</summary>
    public static readonly StyledProperty<string?> DismissTextProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(DismissText));

    public static readonly StyledProperty<IBrush?> TitleForegroundProperty =
        AvaloniaProperty.Register<Banner, IBrush?>(nameof(TitleForeground));

    public static readonly StyledProperty<IBrush?> ActionForegroundProperty =
        AvaloniaProperty.Register<Banner, IBrush?>(nameof(ActionForeground));

    public static readonly StyledProperty<IBrush?> DismissForegroundProperty =
        AvaloniaProperty.Register<Banner, IBrush?>(nameof(DismissForeground));

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

    public string? SecondaryActionText
    {
        get => GetValue(SecondaryActionTextProperty);
        set => SetValue(SecondaryActionTextProperty, value);
    }

    public ICommand? SecondaryActionCommand
    {
        get => GetValue(SecondaryActionCommandProperty);
        set => SetValue(SecondaryActionCommandProperty, value);
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

    public IBrush? DismissForeground
    {
        get => GetValue(DismissForegroundProperty);
        set => SetValue(DismissForegroundProperty, value);
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
