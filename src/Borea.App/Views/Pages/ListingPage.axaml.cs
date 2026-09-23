using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Borea.App.ViewModels;

namespace Borea.App.Views.Pages;

public partial class ListingPage : UserControl
{
    public ListingPage()
    {
        InitializeComponent();
        // tunnelled, because the list takes Enter itself
        ListedSearch.AddHandler(KeyDownEvent, OnListedKeyDown, RoutingStrategies.Tunnel);
        ListedResults.AddHandler(KeyDownEvent, OnListedKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnStepClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string key)
            return;

        this.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => Equals(border.Tag, key))?.BringIntoView();
    }

    /// <summary>The arrow keys in the search field move through the results, and Enter loads the chosen one.</summary>
    private void OnListedKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { ListingEditor: var editor })
            return;

        switch (e.Key)
        {
            case Key.Down when sender == ListedSearch:
                editor.MoveListedSelection(1);
                break;
            case Key.Up when sender == ListedSearch:
                editor.MoveListedSelection(-1);
                break;
            case Key.Enter:
                editor.LoadListedCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
