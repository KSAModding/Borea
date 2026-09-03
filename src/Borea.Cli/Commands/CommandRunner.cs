using System.CommandLine;

namespace Borea.Cli.Commands;

/// <summary>
/// Runs one command body against freshly built services and turns a failed
/// operation into <see cref="ExitCodes.Failed"/> with the reason on stderr.
/// </summary>
internal static class CommandRunner
{
    public static async Task<int> RunAsync(
        ParseResult parseResult,
        Func<CancellationToken, Task<CliServices>> buildServices,
        CancellationToken cancellationToken,
        Func<CliServices, TextWriter, TextWriter, CancellationToken, Task<int>> body)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;

        CliServices services;
        try
        {
            services = await buildServices(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The settings file is the only thing a build reads, so whatever
            // stopped it is about that file.
            error.WriteLine($"error: Borea's settings could not be read. {exception.Message}");
            return ExitCodes.Failed;
        }

        using (services)
        {
            try
            {
                return await body(services, output, error, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                error.WriteLine("error: The command was cancelled.");
                return ExitCodes.Failed;
            }
            catch (Exception exception) when (IsOperationFailure(exception))
            {
                error.WriteLine($"error: {exception.Message}");
                return ExitCodes.Failed;
            }
        }
    }

    /// <summary>
    /// The exceptions a Core operation, a data file, or a remote host raise when
    /// the operation itself fails. A cancellation that is not the user's is a
    /// timeout. Anything else is a defect and keeps its stack trace.
    /// </summary>
    private static bool IsOperationFailure(Exception exception) =>
        exception is InvalidOperationException
            or FormatException
            or IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or OperationCanceledException;
}
