using System.CommandLine;
using Borea.Core.Index;

namespace Borea.Cli.Commands;

internal static class IndexCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var index = new Command("index", "Manage the cached content index.");
        index.Subcommands.Add(BuildRefresh(services));
        return index;
    }

    private static Command BuildRefresh(Func<CancellationToken, Task<CliServices>> services)
    {
        var refresh = new Command("refresh", "Download the content index when it changed.");

        refresh.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var result = await cli.IndexFetcher.FetchAsync(cli.Paths.GetIndexPath(), ct).ConfigureAwait(false);
            output.WriteLine(result is ContentIndexFetchResult.Downloaded ? "Content index: downloaded." : "Content index: not modified.");
            return ExitCodes.Done;
        }));

        return refresh;
    }
}
