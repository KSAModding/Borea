using System.CommandLine;
using System.Globalization;
using Borea.Core.Index;
using Borea.Core.Launch;

namespace Borea.Cli.Commands;

/// <summary>
/// Runs one command body against freshly built services and turns a failed
/// operation into <see cref="ExitCodes.Failed"/> with the reason on stderr.
/// </summary>
internal static class CommandRunner
{
    public static Task<int> RunAsync(
        ParseResult parseResult,
        Func<CancellationToken, Task<CliServices>> buildServices,
        CancellationToken cancellationToken,
        Func<CliServices, TextWriter, TextWriter, CancellationToken, Task<int>> body)
        => RunAsync(parseResult, passThrough: null, buildServices, cancellationToken, body);

    /// <summary>Runs like the overload above, and logs the arguments after "--" with the command.</summary>
    public static async Task<int> RunAsync(
        ParseResult parseResult,
        PassThroughArguments? passThrough,
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
            var passedThrough = passThrough is { Values.Count: > 0 } ? " -- " + ArgumentLine.Join(passThrough.Values) : "";
            services.Log.Write("Command: borea " + string.Join(" ", parseResult.Tokens.Select(token => token.Value)) + passedThrough);
            try
            {
                var exitCode = await body(services, output, error, cancellationToken).ConfigureAwait(false);
                services.Log.Write($"Command finished with exit code {exitCode}.");
                return exitCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                services.Log.Write("Command cancelled.");
                error.WriteLine("error: The command was cancelled.");
                return ExitCodes.Failed;
            }
            catch (Exception exception) when (IsOperationFailure(exception))
            {
                services.Log.Write("Command failed.", exception);
                error.WriteLine($"error: {exception.Message}");
                return ExitCodes.Failed;
            }
            catch (Exception exception) when (LogDefect(services, exception))
            {
                // the filter returns false, so the defect keeps its stack trace
                throw;
            }
            finally
            {
                WriteStaleIndexWarning(services.IndexRefresh.Status, error);
            }
        }
    }

    private static bool LogDefect(CliServices services, Exception exception)
    {
        services.Log.Write("Command stopped on an unexpected error.", exception);
        return false;
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
            or NotSupportedException
            or OperationCanceledException;

    /// <summary>
    /// Only a command that read the index refreshes it, so <c>index refresh</c>,
    /// which reports its own error, never gets this line.
    /// </summary>
    private static void WriteStaleIndexWarning(ContentIndexRefreshStatus status, TextWriter error)
    {
        if (status is { Outcome: ContentIndexRefreshOutcome.Failed, CachedAt: { } cachedAt })
            error.WriteLine($"warning: using the cached index from {cachedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC, the refresh failed: {status.FailureReason}");
    }
}
