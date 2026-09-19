using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Borea.App.Views.Pages;

public partial class ListingPage : UserControl
{
    public ListingPage()
    {
        InitializeComponent();
    }

    private void OnStepClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string key)
            return;

        this.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => Equals(border.Tag, key))?.BringIntoView();
    }
}
