using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.ViewModels;

namespace Borea.App.Views.Pages;

public partial class InstancePage : UserControl
{
    /// <summary>How far the body scrolls down before the header turns compact.</summary>
    private const double CompactFromOffset = 120;

    /// <summary>How near the top the body scrolls before the header turns full again. The gap to <see cref="CompactFromOffset"/> keeps the header from flipping back and forth when the resize of the header moves the body.</summary>
    private const double FullBelowOffset = 8;

    private MainViewModel? _viewModel;

    public InstancePage()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // a page that was just opened has no row in place before the next layout pass
        if (e.PropertyName == nameof(MainViewModel.GameSaveInView) && _viewModel?.GameSaveInView is { } item)
            Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => ReferenceEquals(border.Tag, item))?.BringIntoView(), DispatcherPriority.Loaded);

        // this page stays alive while another page shows, so another instance opens at the top with the full header
        if (e.PropertyName == nameof(MainViewModel.SelectedInstance))
        {
            BodyScroller.ScrollToHome();
            InstanceRoot.Classes.Remove("compact");
        }
    }

    private void OnBodyScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer body)
            return;

        var compact = InstanceRoot.Classes.Contains("compact");
        if (!compact)
        {
            // a short body that could not stay scrolled once the header shrank would flip the header back, so only a body with room beyond the header height turns it compact
            var room = body.Extent.Height - body.Viewport.Height;
            if (body.Offset.Y > CompactFromOffset && room > InstanceRoot.Children[0].Bounds.Height + CompactFromOffset)
            {
                // the breadcrumb and the tabs hide, and a hidden control loses the focus, so the focus goes to the body
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused
                    && focused.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Classes.Contains("full-only")))
                    body.Focus();
                InstanceRoot.Classes.Add("compact");
            }
        }
        else if (body.Offset.Y < FullBelowOffset)
        {
            InstanceRoot.Classes.Remove("compact");
        }
    }

    private async void CopyGameLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { GameLogText: { } text } viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(text);
        viewModel.ReportGameLogCopied();
    }
}
