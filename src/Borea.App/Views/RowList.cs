using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Borea.App.Views;

/// <summary>
/// An <see cref="ItemsControl"/> whose recycled rows keep the controls that the
/// item template built. The stock container drops its child when the panel
/// recycles it, so each row that scrolls back into view is built again, and a
/// fast scroll then makes so much garbage that the collector stalls the list.
/// </summary>
public sealed class RowList : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new RowListItem();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        if (container is RowListItem row)
            row.Show(item, this.FindDataTemplate(item, ItemTemplate) ?? FuncDataTemplate.Default);
        else
            base.PrepareContainerForItemOverride(container, item, index);
    }
}

public sealed class RowListItem : Decorator
{
    private IDataTemplate? _template;

    /// <summary>
    /// Shows <paramref name="item"/>. The row keeps its controls while the same
    /// template shows the item, so only its bindings move to the new item.
    /// </summary>
    internal void Show(object? item, IDataTemplate template)
    {
        DataContext = item;
        if (Child is not null && template == _template)
            return;

        _template = template;
        Child = template.Build(item);
    }
}
