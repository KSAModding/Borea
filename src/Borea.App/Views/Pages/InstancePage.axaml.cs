using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.ViewModels;

namespace Borea.App.Views.Pages;

public partial class InstancePage : UserControl
{
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
    }

    private async void CopyGameLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { GameLogText: { } text } viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(text);
        viewModel.ReportGameLogCopied();
    }
}
