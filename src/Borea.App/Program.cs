using Avalonia;
using Borea.App.Localization;
using Borea.App.SingleInstance;
using Borea.App.ViewModels;
using Borea.Cli;
using Borea.Composition;
using Borea.Core.Links;
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
    /// Any argument runs the command line, unless the first one is a borea: link, which the App checks.
    /// Otherwise the App opens, and first closes a console that Windows created only for it. A double-click
    /// leaves such a console on Windows versions that ignore the console allocation policy in app.manifest.
    /// </summary>
    internal static StartMode ChooseStartMode(string[] args, bool consoleOwnedAlone)
    {
        if (args.Length > 0 && !BoreaLink.HasScheme(args[0]))
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

        var inbox = new StartArgumentsInbox();
        inbox.Post(new StartArguments(args, Forwarded: false));
        var election = AppElection.RunAsync(
                services.Paths.GetAppLockPath(),
                args,
                forwarded => inbox.Post(new StartArguments(forwarded, Forwarded: true)),
                services.Log.Write,
                HandoverTimeouts.Default)
            .GetAwaiter()
            .GetResult();

        if (ExitCodeWithoutApp(election, services.Log.Write, message => ShowHandoverFailure(services, message)) is { } exitCode)
            return exitCode;

        using var primary = election.Primary!;
        return BuildAvaloniaApp(services, inbox, primary).StartWithClassicDesktopLifetime(args);
    }

    /// <returns>Null when this process opens the App.</returns>
    internal static int? ExitCodeWithoutApp(ElectionResult election, Action<string> log, Action<string> showFailure)
    {
        if (election.HandedToProcessId is { } processId)
        {
            log($"Handed this start to the running Borea App, process {processId}.");
            return 0;
        }

        if (election.Primary is not null)
            return null;

        // A second App on the same library could write the same files, so a failed handover ends here.
        var message = $"Borea runs already and did not take this start: {election.Failure}";
        log(message);
        showFailure(message);
        return AppElection.HandoverFailedExitCode;
    }

    private static void ShowHandoverFailure(BoreaServices services, string message)
    {
        Console.Error.WriteLine(message);
        if (!HandoverFailureDialog.IsNeeded)
            return;

        var localization = new LocalizationService();
        var preferences = services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames).GetAwaiter().GetResult().Preferences;
        if (preferences.UiCultureName is not null)
            localization.TrySetCulture(preferences.UiCultureName);

        HandoverFailureDialog.Show(localization.HandoverFailed);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    // The designer calls this method instead of Main, so its App gets no services.
    public static AppBuilder BuildAvaloniaApp()
        => Configure(AppBuilder.Configure<App>());

    private static AppBuilder BuildAvaloniaApp(BoreaServices services, StartArgumentsInbox startArguments, PrimaryInstance primary)
        => Configure(AppBuilder.Configure(() => new App(services, startArguments, primary)));

    private static AppBuilder Configure(AppBuilder builder)
        => builder
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
