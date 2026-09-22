using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Borea.App.Formatting;
using Borea.App.Links;
using Borea.App.Localization;
using Borea.App.SingleInstance;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Composition;
using Borea.Core.Links;
using Borea.Core.Logging;
using Borea.Core.Preferences;
using Borea.Core.Updates;
using Borea.Storage.Updates;

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

    private readonly AppPreferencesLoadStatus _preferencesLoadStatus = AppPreferencesLoadStatus.Loaded;

    private readonly StartArgumentsInbox? _startArguments;

    private readonly PrimaryInstance? _primary;

    private readonly PendingHandover? _pendingHandover;

    private readonly SelfUpdateHandover? _selfUpdateHandover;

    private WindowFront? _windowFront;

    private MainViewModel? _viewModel;

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
        _preferencesLoadStatus = loadResult.Status;
        if (_preferences.UiCultureName is not null)
            Localization.TrySetCulture(_preferences.UiCultureName);

        RegionalFormat = new RegionalFormatService(
            Localization,
            System.Globalization.CultureInfo.CurrentCulture,
            _preferences.RegionalCultureName);
    }

    /// <param name="selfUpdateHandover">The build this one replaced, when a self-update started it. Null otherwise.</param>
    internal App(
        BoreaServices services,
        StartArgumentsInbox startArguments,
        PrimaryInstance primary,
        PendingHandover pendingHandover,
        SelfUpdateHandover? selfUpdateHandover = null)
        : this(services)
    {
        _startArguments = startArguments ?? throw new ArgumentNullException(nameof(startArguments));
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _pendingHandover = pendingHandover ?? throw new ArgumentNullException(nameof(pendingHandover));
        _selfUpdateHandover = selfUpdateHandover;
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
                Services)
            {
                PreferencesLoadStatus = _preferencesLoadStatus,
                LinkHandler = Services is null ? null : LinkHandler.ForThisProcess(),
                EndApp = () => desktop.Shutdown(),
                PendingHandover = _pendingHandover,
            };
            _viewModel = viewModel;

            ApplyTheme(viewModel.CurrentTheme);
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;
            _windowFront = new WindowFront(window);
            window.Opened += OnMainWindowOpened;
            window.Opened += async (_, _) => await viewModel.LoadAsync();
            window.Closing += OnMainWindowClosing;
            if (Services is { } services)
            {
                window.Opened += (_, _) => ExtractionFolderCleanup.StartForThisProcess(services.Log);
                window.Opened += (_, _) => SweepUpdateLeftovers(services.Log);
                if (_selfUpdateHandover is { } selfUpdateHandover)
                    window.Opened += (_, _) => RemoveReplacedBuild(selfUpdateHandover, services.Log);
            }

            // players come back to Borea after installing a new KSA release
            desktop.MainWindow.Activated += async (_, _) => await viewModel.RefreshInstalledGameAsync();

            // a command that throws must not take the window down with it
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                Services?.Log.Write("Unhandled exception on the UI thread.", args.Exception);
                viewModel.ShowUnexpectedError(args.Exception);
                args.Handled = true;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Removes the build that this one replaced. It runs when the window stands, so a new build which
    /// cannot open one leaves the old build in place, and it runs off the UI thread because it waits
    /// for the old process to end.
    /// </summary>
    private static void RemoveReplacedBuild(SelfUpdateHandover handover, IBoreaLog log)
        => Task.Run(() => log.Write(SelfUpdateCleanup.Run(handover)));

    /// <summary>
    /// Removes what an update that never finished left in the folder Borea runs in. A crash or a
    /// power loss inside an update leaves the unpacked build there, and no other step takes it.
    /// </summary>
    private static void SweepUpdateLeftovers(IBoreaLog log)
        => Task.Run(() =>
        {
            if (SelfUpdateCleanup.SweepStaging() is { } message)
                log.Write(message);
        });

    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        ((Window)sender!).Opened -= OnMainWindowOpened;
        _startArguments?.Open(start => Dispatcher.UIThread.Post(() => ReceiveStartArguments(start)));
    }

    // MainWindow cancels a close only to close later, so an uncancelled Closing means the window goes away.
    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!e.Cancel)
            _ = _primary?.StopTakingStartsAsync();
    }

    /// <summary>Every start arrives here on the UI thread once the main window is open, the own start first.</summary>
    private void ReceiveStartArguments(StartArguments start)
    {
        if (start.Forwarded)
            _windowFront?.BringToFront();

        if (start.Arguments is [var first, ..] && BoreaLink.HasScheme(first) && _viewModel is { } viewModel)
            Dispatcher.UIThread.Post(async () => await viewModel.OpenStartLinkAsync(start.Arguments));
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
