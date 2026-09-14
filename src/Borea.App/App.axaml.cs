using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Composition;
using Borea.Core.Preferences;

namespace Borea.App;

public partial class App : Application
{
    /// <summary>
    /// The services Main built from the saved settings. Null only in the XAML
    /// previewer, which builds the App without going through Main.
    /// </summary>
    public BoreaServices? Services { get; }

    public LocalizationService Localization { get; }

    public RegionalFormatService RegionalFormat { get; }

    private readonly AppPreferences _preferences;

    public App()
    {
        Localization = new LocalizationService();
        RegionalFormat = new RegionalFormatService(Localization);
        _preferences = AppPreferences.Empty;
    }

    public App(BoreaServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Localization = new LocalizationService();
        var loadResult = Services.AppPreferences
            .GetAsync(MainViewModel.BundledThemeNames)
            .GetAwaiter()
            .GetResult();
        _preferences = loadResult.Preferences;
        if (_preferences.UiCultureName is not null)
            Localization.TrySetCulture(_preferences.UiCultureName);

        RegionalFormat = new RegionalFormatService(
            Localization,
            System.Globalization.CultureInfo.CurrentCulture,
            _preferences.RegionalCultureName);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services?.Log.Write($"Borea {MainViewModel.BoreaInformationalVersion} started. {MainViewModel.RuntimeText}, {MainViewModel.SystemText}.");
            var viewModel = new MainViewModel(
                Localization,
                RegionalFormat,
                Services?.AppPreferences,
                _preferences,
                Services);

            ApplyTheme(viewModel.CurrentTheme);
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow.Opened += async (_, _) => await viewModel.LoadAsync();

            // players come back to Borea after installing a new KSA release
            desktop.MainWindow.Activated += async (_, _) => await viewModel.RefreshInstalledGameAsync();

            // a command that throws must not take the window down with it
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                Services?.Log.Write("Unhandled exception on the UI thread.", args.Exception);
                viewModel.UnexpectedError = args.Exception.Message;
                args.Handled = true;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentTheme) && sender is MainViewModel viewModel)
            ApplyTheme(viewModel.CurrentTheme);
    }

    /// <summary>
    /// Every bundled theme is one of Avalonia's variants; App.axaml holds the
    /// palette for each under its ThemeDictionaries.
    /// </summary>
    private void ApplyTheme(string themeName)
    {
        RequestedThemeVariant = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
