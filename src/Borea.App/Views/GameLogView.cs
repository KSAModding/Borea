using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Borea.App.ViewModels;

namespace Borea.App.Views;

/// <summary>
/// The game log as one selectable text, so a selection can span lines. Each
/// line keeps its colored parts as runs.
/// </summary>
public sealed class GameLogView : SelectableTextBlock
{
    public static readonly StyledProperty<IEnumerable<GameLogLine>?> LinesProperty =
        AvaloniaProperty.Register<GameLogView, IEnumerable<GameLogLine>?>(nameof(Lines));

    private INotifyCollectionChanged? _observed;
    private bool _rebuildPending;

    // the theme and the log-line style are written for SelectableTextBlock
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public IEnumerable<GameLogLine>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LinesProperty)
            return;

        Unobserve();
        if (VisualRoot is not null)
            Observe();

        Rebuild();
    }

    // listen only while shown, so a page that is gone does not keep the view alive through the collection
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Observe();
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unobserve();
        base.OnDetachedFromVisualTree(e);
    }

    private void Observe()
    {
        Unobserve();
        _observed = Lines as INotifyCollectionChanged;
        if (_observed is not null)
            _observed.CollectionChanged += OnLinesChanged;
    }

    private void Unobserve()
    {
        if (_observed is not null)
            _observed.CollectionChanged -= OnLinesChanged;

        _observed = null;
    }

    // a load clears the collection and adds every line one by one; the text is built once after that
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildPending)
            return;

        _rebuildPending = true;
        Dispatcher.UIThread.Post(Rebuild, DispatcherPriority.Background);
    }

    /// <summary>
    /// A reload replaces every line, so the text is built again as a whole and
    /// the old selection goes with it.
    /// </summary>
    private void Rebuild()
    {
        _rebuildPending = false;
        ClearSelection();
        var inlines = new InlineCollection();
        var first = true;
        foreach (var entry in Lines ?? Array.Empty<GameLogLine>())
        {
            if (!first)
                inlines.Add(new LineBreak());
            first = false;

            Add(inlines, entry.Time, "log-muted");
            Add(inlines, entry.Level, SeverityClass(entry, forLevel: true));
            Add(inlines, entry.Tag, "log-tag");
            Add(inlines, entry.Message, SeverityClass(entry, forLevel: false));
        }

        Inlines = inlines;
    }

    private static void Add(InlineCollection inlines, string text, string? styleClass)
    {
        if (text.Length == 0)
            return;

        var run = new Run(text);
        if (styleClass is not null)
            run.Classes.Add(styleClass);
        inlines.Add(run);
    }

    /// <summary>The same classes the per-line template set: the level is always colored, the message only when it is not plain information.</summary>
    private static string? SeverityClass(GameLogLine line, bool forLevel) =>
        line.IsMuted ? "log-muted"
        : line.IsError ? "log-error"
        : line.IsWarning ? "log-warning"
        : forLevel && line.IsInformation ? "log-information"
        : null;
}
