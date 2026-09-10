using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Composition;

namespace Borea.App;

public partial class App : Application
{
    /// <summary>
    /// The services Main built from the saved settings. Null only in the XAML
    /// previewer, which builds the App without going through Main.
    /// </summary>
    public BoreaServices? Services { get; }

    public LocalizationService Localization { get; }

    public App()
    {
        Localization = new LocalizationService();
    }

    public App(BoreaServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Localization = new LocalizationService();
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
                DataContext = new MainViewModel(Localization),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
