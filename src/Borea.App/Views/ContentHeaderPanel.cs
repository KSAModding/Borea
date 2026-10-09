using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

/// <summary>Where a <see cref="ContentHeaderPanel"/> puts its actions.</summary>
public enum ContentHeaderActionsPlace
{
    /// <summary>Right of the text, at the top.</summary>
    BesideText,

    /// <summary>Below the text, in the column right of the image.</summary>
    BelowText,

    /// <summary>Below the image and the column, across the full width, because the column is too narrow for the actions.</summary>
    BelowAll,
}

/// <summary>
/// Lays out the header of a content or pack page. The children are, in order,
/// the image, the text, the actions and the meta row. The text and the meta row
/// sit in a column right of the image, and the meta row goes down to the bottom
/// of the image when the text is short. The actions sit right of the text only
/// when the column is at least <see cref="BesideMinColumnWidth"/> wide and the
/// text keeps at least <see cref="MinTextWidth"/> beside them, so a long button
/// text or a long translation moves the actions instead of squeezing the title.
/// Otherwise they wrap below the text when the column is at least
/// <see cref="BelowTextMinColumnWidth"/> wide and holds their widest part, or
/// they go below the whole header, so every button shows whole. The fixed
/// column widths keep the actions in one place while the buttons change during
/// an install, and the measured widths are the fallback for a longer
/// translation.
/// </summary>
public sealed class ContentHeaderPanel : Panel
{
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ContentHeaderPanel, double>(nameof(ColumnSpacing), 32);

    public static readonly StyledProperty<double> ActionsSpacingProperty =
        AvaloniaProperty.Register<ContentHeaderPanel, double>(nameof(ActionsSpacing), 24);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ContentHeaderPanel, double>(nameof(RowSpacing), 16);

    public static readonly StyledProperty<double> MinTextWidthProperty =
        AvaloniaProperty.Register<ContentHeaderPanel, double>(nameof(MinTextWidth), 280);

    public static readonly StyledProperty<double> BesideMinColumnWidthProperty =
        AvaloniaProperty.Register<ContentHeaderPanel, double>(nameof(BesideMinColumnWidth), 760);

    public static readonly StyledProperty<double> BelowTextMinColumnWidthProperty =
        AvaloniaProperty.Register<ContentHeaderPanel, double>(nameof(BelowTextMinColumnWidth), 280);

    private Rect _text;
    private Rect _actions;
    private Rect _meta;

    static ContentHeaderPanel()
    {
        AffectsMeasure<ContentHeaderPanel>(ColumnSpacingProperty, ActionsSpacingProperty, RowSpacingProperty, MinTextWidthProperty, BesideMinColumnWidthProperty, BelowTextMinColumnWidthProperty);
    }

    /// <summary>The space between the image and the column of the text.</summary>
    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>The space between the text and the actions beside it.</summary>
    public double ActionsSpacing
    {
        get => GetValue(ActionsSpacingProperty);
        set => SetValue(ActionsSpacingProperty, value);
    }

    /// <summary>The space above the actions when they are below the text or the image.</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>The least width the text keeps beside the actions.</summary>
    public double MinTextWidth
    {
        get => GetValue(MinTextWidthProperty);
        set => SetValue(MinTextWidthProperty, value);
    }

    /// <summary>
    /// The least width of the column right of the image in which the actions sit
    /// beside the text. It holds the widest actions of every state of today's
    /// pages next to <see cref="MinTextWidth"/>, so the actions do not move when
    /// the buttons change.
    /// </summary>
    public double BesideMinColumnWidth
    {
        get => GetValue(BesideMinColumnWidthProperty);
        set => SetValue(BesideMinColumnWidthProperty, value);
    }

    /// <summary>
    /// The least width of the column right of the image in which the actions wrap
    /// below the text. It holds the widest button of every state of today's pages,
    /// so the actions do not move between the column and the full width when the
    /// buttons change.
    /// </summary>
    public double BelowTextMinColumnWidth
    {
        get => GetValue(BelowTextMinColumnWidthProperty);
        set => SetValue(BelowTextMinColumnWidthProperty, value);
    }

    /// <summary>Where the last layout put the actions.</summary>
    public ContentHeaderActionsPlace ActionsPlace { get; private set; }

    /// <summary>
    /// Where actions that are <paramref name="actionsWidth"/> wide on one line, and
    /// <paramref name="widestPart"/> wide at their widest part that cannot wrap,
    /// go in a column that is <paramref name="columnWidth"/> wide.
    /// </summary>
    private ContentHeaderActionsPlace Place(double columnWidth, double actionsWidth, double widestPart) =>
        actionsWidth <= 0 || (columnWidth >= BesideMinColumnWidth && columnWidth - ActionsSpacing - actionsWidth >= MinTextWidth) ? ContentHeaderActionsPlace.BesideText
        : columnWidth >= BelowTextMinColumnWidth && widestPart <= columnWidth ? ContentHeaderActionsPlace.BelowText
        : ContentHeaderActionsPlace.BelowAll;

    /// <summary>
    /// The width of the widest part of <paramref name="control"/> that cannot wrap.
    /// A wrap panel can put each child on its own line, and a plain panel shows its
    /// children on top of each other, so both give their widest visible child.
    /// </summary>
    private static double WidestPart(Control control) =>
        control is WrapPanel || control.GetType() == typeof(Panel)
            ? ((Panel)control).Children.Where(child => child.IsVisible).Select(WidestPart).DefaultIfEmpty(0).Max()
            : control.DesiredSize.Width;

    private Control? Child(int index) => Children.Count > index && Children[index].IsVisible ? Children[index] : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var (image, text, actions, meta) = (Child(0), Child(1), Child(2), Child(3));
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        image?.Measure(infinite);
        var imageSize = image?.DesiredSize ?? default;
        var left = image is null ? 0 : imageSize.Width + ColumnSpacing;

        actions?.Measure(infinite);
        var actionsWidth = actions?.DesiredSize.Width ?? 0;
        var widestPart = actions is null ? 0 : WidestPart(actions);

        var width = double.IsInfinity(availableSize.Width) ? left + Math.Max(BesideMinColumnWidth, actionsWidth + ActionsSpacing + MinTextWidth) : availableSize.Width;
        var columnWidth = Math.Max(0, width - left);
        ActionsPlace = Place(columnWidth, actionsWidth, widestPart);

        var textWidth = ActionsPlace == ContentHeaderActionsPlace.BesideText && actions is not null ? Math.Max(0, columnWidth - ActionsSpacing - actionsWidth) : columnWidth;
        text?.Measure(new Size(textWidth, double.PositiveInfinity));
        var textHeight = text?.DesiredSize.Height ?? 0;
        meta?.Measure(new Size(columnWidth, double.PositiveInfinity));
        var metaHeight = meta?.DesiredSize.Height ?? 0;

        double top;
        switch (ActionsPlace)
        {
            case ContentHeaderActionsPlace.BesideText:
                _text = new Rect(left, 0, textWidth, textHeight);
                _actions = actions is null ? default : new Rect(width - actionsWidth, 0, actionsWidth, actions.DesiredSize.Height);
                top = Math.Max(textHeight, _actions.Height);
                break;
            case ContentHeaderActionsPlace.BelowText:
                actions!.Measure(new Size(columnWidth, double.PositiveInfinity));
                _text = new Rect(left, 0, columnWidth, textHeight);
                _actions = new Rect(left, textHeight + RowSpacing, Math.Min(columnWidth, actions.DesiredSize.Width), actions.DesiredSize.Height);
                top = _actions.Bottom;
                break;
            default:
                _text = new Rect(left, 0, columnWidth, textHeight);
                top = textHeight;
                break;
        }

        // the meta row goes to the bottom of the image, as far as the text above it leaves room
        _meta = new Rect(left, Math.Max(top, imageSize.Height - metaHeight), columnWidth, metaHeight);
        var height = Math.Max(imageSize.Height, _meta.Bottom);
        if (ActionsPlace == ContentHeaderActionsPlace.BelowAll)
        {
            actions!.Measure(new Size(width, double.PositiveInfinity));
            _actions = new Rect(0, height + RowSpacing, Math.Min(width, actions.DesiredSize.Width), actions.DesiredSize.Height);
            height = _actions.Bottom;
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var image = Child(0);
        image?.Arrange(new Rect(image.DesiredSize));
        Child(1)?.Arrange(_text);
        Child(2)?.Arrange(_actions);
        Child(3)?.Arrange(_meta);
        return finalSize;
    }
}
