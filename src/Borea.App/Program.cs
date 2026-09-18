using Avalonia;
using Borea.Cli;
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
        switch (ChooseStartMode(args, WindowsConsole.IsOwnedAlone()))
        {
            case StartMode.Cli:
                return Cli.Program.RunAsync(args).GetAwaiter().GetResult();
            case StartMode.AppWithoutConsole:
                WindowsConsole.Free();
                break;
        }

        return RunApp(args);
    }

    /// <summary>
    /// Any argument runs the command line. Without one the App opens, and first closes a console
    /// that Windows created only for it. A double-click leaves such a console on Windows versions
    /// that ignore the console allocation policy in app.manifest.
    /// </summary>
    internal static StartMode ChooseStartMode(string[] args, bool consoleOwnedAlone)
    {
        if (args.Length > 0)
            return StartMode.Cli;

        return consoleOwnedAlone ? StartMode.AppWithoutConsole : StartMode.App;
    }

    private static int RunApp(string[] args)
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
