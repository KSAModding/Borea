using System.Text;
using Borea.Composition;
using Borea.Core.Logging;
using Borea.Core.Updates;
using Borea.Storage.Logging;
using Borea.Storage.Paths;
using Borea.Storage.Updates;

namespace Borea.Cli;

/// <summary>
/// The entry point of the <c>borea</c> command. The App runs <see cref="RunAsync"/> when it starts with arguments.
/// </summary>
public static class Program
{
    internal const string StartHint = """
        This is the Borea command line.
        Run borea --help in a terminal to list the commands.
        The desktop App is in the release archive whose name does not start with Borea-Cli-.
        Press Enter to close this window.
        """;

    private static async Task<int> Main(string[] args)
    {
        // A self-update starts this build with the old one as an argument, and the old files go last.
        var handover = SelfUpdateHandover.Take(ref args);
        if (handover is not null && args.Length == 0)
            return FinishSelfUpdate(handover);

        if (ShowsStartHint(args, WindowsConsole.IsOwnedAlone(), Console.IsInputRedirected, Console.IsOutputRedirected))
        {
            Console.WriteLine(StartHint);
            Console.ReadLine();
            return ExitCodes.Usage;
        }

        var exitCode = await RunAsync(args).ConfigureAwait(false);
        if (handover is not null)
            FinishSelfUpdate(handover);

        return exitCode;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        using var output = Console.IsOutputRedirected ? Utf8Writer(Console.OpenStandardOutput()) : null;
        using var error = Console.IsErrorRedirected ? Utf8Writer(Console.OpenStandardError()) : null;

        return await BoreaCli.RunAsync(args, BuildServicesAsync, output ?? Console.Out, error ?? Console.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the build this one replaced, after this build has shown that it runs. Only the log says
    /// what happened, because a player who updated from the App never sees this process.
    /// </summary>
    /// <returns>The exit code of a start that did nothing else.</returns>
    public static int FinishSelfUpdate(SelfUpdateHandover handover)
    {
        var message = SelfUpdateCleanup.Run(handover);
        new FileBoreaLog(new GamePathProvider(gameDirectory: null), BoreaLogSource.Cli).Write(message);
        return ExitCodes.Done;
    }

    /// <summary>
    /// A double-click starts the command in a console window of its own, which would close
    /// before anyone could read the usage error. A program that starts borea with redirected
    /// input or output gets the usage error, because nobody could read the hint or press Enter.
    /// </summary>
    internal static bool ShowsStartHint(string[] args, bool consoleOwnedAlone, bool inputRedirected, bool outputRedirected)
        => args.Length == 0 && consoleOwnedAlone && !inputRedirected && !outputRedirected;

    private static async Task<CliServices> BuildServicesAsync(CancellationToken cancellationToken)
        => CliServices.From(await BoreaServices.BuildAsync(boreaRoot: null, BoreaLogSource.Cli, cancellationToken).ConfigureAwait(false));

    private static StreamWriter Utf8Writer(Stream stream)
        => new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
}
