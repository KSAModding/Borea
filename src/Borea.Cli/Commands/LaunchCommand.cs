using System.CommandLine;

namespace Borea.Cli.Commands;

internal static class LaunchCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", "The instance's name, or its id when two names differ only in case.");
        var loaderId = ArgumentRules.ContentId("loader-id", "The installed mod loader to use.");
        var launch = new Command("launch", "Start one instance through an installed mod loader.");
        launch.Arguments.Add(instance);
        launch.Arguments.Add(loaderId);

        launch.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup
                .ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance))
                .ConfigureAwait(false);
            var loader = await LoaderLookup
                .GetListingAsync(cli.Mods, parseResult.GetRequiredValue(loaderId), ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var result = cli.Launcher.Launch(target, loader);

            if (!result.Started)
            {
                error.WriteLine($"error: {result.Message}");
                return ExitCodes.Failed;
            }

            output.WriteLine(result.Message);
            output.WriteLine($"Process id: {result.ProcessId}");
            return ExitCodes.Done;
        }));

        return launch;
    }
}
