using Avalonia;
using Borea.Composition;
using System;

namespace Borea.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        using var services = BoreaServices.BuildAsync().GetAwaiter().GetResult();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                services.Log.Write("Unhandled exception.", exception);
        };

        return BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    // The designer calls this method instead of Main, so its App gets no services.
    public static AppBuilder BuildAvaloniaApp()
        => Configure(AppBuilder.Configure<App>());

    private static AppBuilder BuildAvaloniaApp(BoreaServices services)
        => Configure(AppBuilder.Configure(() => new App(services)));

    private static AppBuilder Configure(AppBuilder builder)
        => builder
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
