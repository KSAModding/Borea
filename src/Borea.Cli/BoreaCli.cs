using System.CommandLine;
using Borea.Cli.Commands;

namespace Borea.Cli;

/// <summary>
/// Builds the command tree and runs one command line against it.
/// Every command maps onto one Borea.Core operation and adds no logic of its own.
/// The exit codes are in <see cref="ExitCodes"/>.
/// </summary>
internal static class BoreaCli
{
    /// <summary>
    /// Builds the command tree. The services are built by <paramref name="services"/>.
    /// </summary>
    public static RootCommand Build(Func<CancellationToken, Task<CliServices>> services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        var root = new RootCommand("Borea, the content manager for Kitten Space Agency.");
        root.Subcommands.Add(SettingsCommand.Build(services));
        root.Subcommands.Add(GameCommand.Build(services));
        root.Subcommands.Add(InstanceCommand.Build(services));
        root.Subcommands.Add(ModStateCommands.BuildEnable(services));
        root.Subcommands.Add(ModStateCommands.BuildDisable(services));
        return root;
    }

    /// <summary>
    /// Parses and runs <paramref name="args"/>. A command line that does not parse
    /// exits with <see cref="ExitCodes.Usage"/> after the parser said why.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        Func<CancellationToken, Task<CliServices>> services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args is null)
            throw new ArgumentNullException(nameof(args));

        if (output is null)
            throw new ArgumentNullException(nameof(output));

        if (error is null)
            throw new ArgumentNullException(nameof(error));

        var parseResult = Build(services).Parse(args);
        var configuration = new InvocationConfiguration { Output = output, Error = error };
        var exitCode = await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);

        return parseResult.Errors.Count > 0 ? ExitCodes.Usage : exitCode;
    }
}
