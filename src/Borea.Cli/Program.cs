using System.Text;
using Borea.Composition;
using Borea.Core.Logging;

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
        if (ShowsStartHint(args, WindowsConsole.IsOwnedAlone(), Console.IsInputRedirected, Console.IsOutputRedirected))
        {
            Console.WriteLine(StartHint);
            Console.ReadLine();
            return ExitCodes.Usage;
        }

        return await RunAsync(args).ConfigureAwait(false);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        using var output = Console.IsOutputRedirected ? Utf8Writer(Console.OpenStandardOutput()) : null;
        using var error = Console.IsErrorRedirected ? Utf8Writer(Console.OpenStandardError()) : null;

        return await BoreaCli.RunAsync(args, BuildServicesAsync, output ?? Console.Out, error ?? Console.Error).ConfigureAwait(false);
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
