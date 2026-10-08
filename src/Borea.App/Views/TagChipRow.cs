using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Borea.App.Views;

/// <summary>
/// The tags of a list row as chips on one line. It shows at most <see cref="MaxShown"/> chips, only as many as fit whole,
/// and when some tags are left out a last chip says how many and names them in its tooltip.
/// A row can lead with a green chip of its own, such as Installed, which comes first, counts as one of the chips that fit,
/// and gives way only after every tag and the count chip.
/// </summary>
public sealed class TagChipRow : Panel
{
    internal const int MaxShown = 6;

    private const double Spacing = 4;

    public static readonly StyledProperty<IReadOnlyList<string>?> TagsProperty =
        AvaloniaProperty.Register<TagChipRow, IReadOnlyList<string>?>(nameof(Tags));

    public static readonly StyledProperty<string?> LeadProperty =
        AvaloniaProperty.Register<TagChipRow, string?>(nameof(Lead));

    public static readonly StyledProperty<bool> ShowsLeadProperty =
        AvaloniaProperty.Register<TagChipRow, bool>(nameof(ShowsLead));

    public static readonly StyledProperty<string?> LeadTipProperty =
        AvaloniaProperty.Register<TagChipRow, string?>(nameof(LeadTip));

    private readonly TextBlock _moreText = new();
    private readonly Border _more;
    private readonly TextBlock _leadText = new();
    private readonly Border _lead;
    private int _shown;

    static TagChipRow() => AffectsMeasure<TagChipRow>(TagsProperty, LeadProperty, ShowsLeadProperty);

    public TagChipRow()
    {
        ClipToBounds = true;
        _more = Chip(_moreText);
        _lead = Chip(_leadText);
        _lead.Classes.Add("positive");
        Children.Add(_more);
    }

    public IReadOnlyList<string>? Tags
    {
        get => GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    /// <summary>The text of the green chip that comes before the tags while <see cref="ShowsLead"/> is true.</summary>
    public string? Lead
    {
        get => GetValue(LeadProperty);
        set => SetValue(LeadProperty, value);
    }

    public bool ShowsLead
    {
        get => GetValue(ShowsLeadProperty);
        set => SetValue(ShowsLeadProperty, value);
    }

    public string? LeadTip
    {
        get => GetValue(LeadTipProperty);
        set => SetValue(LeadTipProperty, value);
    }

    /// <summary>How many tags the last layout shows as chips, without the lead chip.</summary>
    internal int ShownCount => Math.Max(0, _shown - LeadCount);

    /// <summary>The chip that names the left out tags, which the layout shows only when there are some.</summary>
    internal Border MoreChip => _more;

    /// <summary>The green chip before the tags.</summary>
    internal Border LeadChip => _lead;

    internal bool ShowsMore { get; private set; }

    private int LeadCount => Children.Contains(_lead) ? 1 : 0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LeadProperty)
            _leadText.Text = Lead;
        else if (change.Property == LeadTipProperty)
            ToolTip.SetTip(_lead, LeadTip);

        if (change.Property != TagsProperty && change.Property != LeadProperty && change.Property != ShowsLeadProperty)
            return;

        var leads = ShowsLead && !string.IsNullOrEmpty(Lead);
        Children.Clear();
        if (leads)
            Children.Add(_lead);
        foreach (var tag in (Tags ?? []).Take(MaxShown - (leads ? 1 : 0)))
            Children.Add(Chip(new TextBlock { Text = tag }));
        Children.Add(_more);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var tags = Tags ?? [];
        var leadCount = LeadCount;
        var chips = Children.Where(child => child != _more).ToList();
        foreach (var chip in chips)
            chip.Measure(Size.Infinity);

        // give up tags from the end until the ones left and the chip that counts the others fit, and keep the lead chip
        var shown = chips.Count;
        var width = Fit(shown);
        while (width > availableSize.Width && shown > leadCount)
            width = Fit(--shown);

        // a lead chip with the count chip that does not fit stays alone, and a lead chip that does not fit alone gives way too,
        // so the row never shows a cut chip
        if (width > availableSize.Width && leadCount > 0)
        {
            ShowsMore = false;
            shown = RowWidth(chips, 1) <= availableSize.Width ? 1 : 0;
            width = RowWidth(chips, shown);
        }

        if (!ShowsMore)
            _more.Measure(Size.Infinity);

        _shown = shown;
        var height = chips.Take(shown).Append(ShowsMore ? _more : null).Max(child => child?.DesiredSize.Height ?? 0);
        return new Size(Math.Min(width, availableSize.Width), height);

        // the width of the first count chips, with the count chip when tags are left out
        double Fit(int count)
        {
            var shownTags = count - leadCount;
            ShowsMore = shownTags < tags.Count;
            if (!ShowsMore)
                return RowWidth(chips, count);

            ShowMore(tags, shownTags);
            return RowWidth(chips, count) + (count > 0 ? Spacing : 0) + _more.DesiredSize.Width;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var index = 0;
        foreach (var child in Children)
        {
            var visible = child == _more ? ShowsMore : index++ < _shown;
            if (!visible)
            {
                // a chip that is left out gets no room, and its own clip draws nothing
                child.Arrange(default);
                continue;
            }

            child.Arrange(new Rect(x, (finalSize.Height - child.DesiredSize.Height) / 2, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }

        return finalSize;
    }

    private static double RowWidth(IReadOnlyList<Control> chips, int count) =>
        chips.Take(count).Sum(chip => chip.DesiredSize.Width) + Spacing * Math.Max(0, count - 1);

    private void ShowMore(IReadOnlyList<string> tags, int shown)
    {
        var left = tags.Skip(shown).ToList();
        var text = $"+{left.Count}";
        if (_moreText.Text != text)
            _moreText.Text = text;
        ToolTip.SetTip(_more, string.Join(", ", left));
        _more.Measure(Size.Infinity);
    }

    private static Border Chip(TextBlock text)
    {
        var chip = new Border { Child = text, ClipToBounds = true };
        chip.Classes.Add("chip");
        return chip;
    }
}
