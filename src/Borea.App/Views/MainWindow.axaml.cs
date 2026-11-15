using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Borea.App.ViewModels;

namespace Borea.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // tunnelled, so a control under the pointer that handles the press cannot keep it from the window
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPrimaryPressed, RoutingStrategies.Tunnel);
        FocusSink.KeyDown += OnFocusSinkKeyDown;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel viewModel)
            viewModel.WindowServices = new WindowServices(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && DataContext is MainViewModel viewModel && !viewModel.RequestClose(Close))
            e.Cancel = true;
    }

    /// <summary>
    /// The back and forward buttons of a mouse (#492). Back acts only where the page
    /// shows the back arrow, and neither acts while a modal is open.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var command = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.XButton1Pressed when viewModel.CanGoBack => viewModel.GoBackCommand,
            PointerUpdateKind.XButton2Pressed => viewModel.GoForwardCommand,
            _ => null,
        };
        if (command is null || IsModalOpen() || !command.CanExecute(null))
            return;

        command.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Avalonia moves the focus on a press only to a control that can take it, so
    /// without this a text box keeps its caret and the keys after a click beside it
    /// (#676). A press on a part of the window that cannot take the focus gives it to
    /// <c>FocusSink</c> instead, on a page and in a modal alike. <c>Focus(null)</c> is
    /// not enough, because the window gives the focus back to the text box when it is
    /// activated again.
    /// </summary>
    private void OnPrimaryPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed
            || FocusManager?.GetFocusedElement() is not TextBox
            || e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().Any(KeepsTheFocus))
            return;

        FocusSink.Focus(NavigationMethod.Pointer);
    }

    /// <summary>
    /// <c>FocusSink</c> sits before the rail, so Tab from it would go on behind an open
    /// modal. While a modal is open, Tab goes to the first control of the top modal and
    /// Shift+Tab to its last one. Escape closes the modal, because the Escape of a modal
    /// sits on its text box, which no longer has the focus.
    /// </summary>
    private void OnFocusSinkKeyDown(object? sender, KeyEventArgs e)
    {
        if (TopModal() is not { } modal)
            return;

        if (e.Key == Key.Escape)
        {
            if (modal.GetVisualAncestors().OfType<Panel>().FirstOrDefault(panel => ModalBackdrop.GetCloseButton(panel) is not null) is { } backdrop)
                e.Handled = ModalBackdrop.TryClose(backdrop);
            return;
        }

        if (e.Key != Key.Tab)
            return;

        // the TopLevel property FocusManager hides the class of the same name
        var target = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            ? Avalonia.Input.FocusManager.FindLastFocusableElement(modal)
            : Avalonia.Input.FocusManager.FindFirstFocusableElement(modal);
        if (target?.Focus(NavigationMethod.Tab) == true)
            e.Handled = true;
    }

    /// <summary>A control that takes the focus itself, or one whose press must leave it where it is.</summary>
    private static bool KeepsTheFocus(Visual visual) =>
        visual is ScrollBar or OverlayPopupHost or MenuBase
        || visual is InputElement { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true };

    internal bool IsModalOpen() => TopModal() is not null;

    /// <summary>The open modal that is drawn over the others, because it comes last in the window.</summary>
    private Border? TopModal() =>
        this.GetVisualDescendants().OfType<Border>().LastOrDefault(border => border.Classes.Contains("modal") && border.IsEffectivelyVisible);
}
