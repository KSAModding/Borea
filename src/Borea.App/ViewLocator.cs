using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Borea.App.Localization;
using Borea.App.ViewModels;

namespace Borea.App;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        var localization = (Application.Current as App)?.Localization ?? new LocalizationService();
        return new LocalizedViewNotFoundTextBlock(localization, name);
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }

    private sealed class LocalizedViewNotFoundTextBlock : TextBlock
    {
        private readonly LocalizationService _localization;
        private readonly string _viewName;

        public LocalizedViewNotFoundTextBlock(LocalizationService localization, string viewName)
        {
            _localization = localization;
            _viewName = viewName;
            UpdateText();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _localization.PropertyChanged += OnLocalizationChanged;
            UpdateText();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _localization.PropertyChanged -= OnLocalizationChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e) => UpdateText();

        private void UpdateText() => Text = _localization.FormatViewNotFound(_viewName);
    }
}
