using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    Localization,
                    RegionalFormat,
                    Services?.AppPreferences,
                    _preferences),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
