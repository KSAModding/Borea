using System.CommandLine;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

internal static class LaunchCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", "The instance's name, or its id when two names differ only in case.");
        var loaderId = ArgumentRules.OptionalContentId("loader-id", "The installed mod loader to use. Omit it when the instance's mods need exactly one loader.");
        var launch = new Command("launch", "Start one instance through an installed mod loader.");
        launch.Arguments.Add(instance);
        launch.Arguments.Add(loaderId);

        launch.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup
                .ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance))
                .ConfigureAwait(false);
            var targetLoaderId = parseResult.GetValue(loaderId);
            if (targetLoaderId is null)
            {
                targetLoaderId = RequiredLoaderId(target);
                output.WriteLine($"The mods in '{target.Name}' need {targetLoaderId}.");
            }

            var loader = await LoaderLookup
                .GetListingAsync(cli.Mods, targetLoaderId, ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var result = cli.Launcher.Launch(target, loader);

            // a loader that stops with an error while the game loads is reported, not left to vanish
            if (result.Started)
                result = await cli.Launcher.WatchStartAsync(target, result, ct).ConfigureAwait(false);

            if (!result.Started)
            {
                error.WriteLine($"error: {result.Message}");
                if (result.Outcome == Borea.Core.Launch.LaunchOutcome.ExitedEarly)
                {
                    error.WriteLine($"{loader.Name} wrote:");
                    foreach (var line in result.Output)
                        error.WriteLine($"  {line}");
                }

                return ExitCodes.Failed;
            }

            output.WriteLine(result.Message);
            output.WriteLine($"Process id: {result.ProcessId}");
            return ExitCodes.Done;
        }));

        return launch;
    }

    private static string RequiredLoaderId(Instance instance)
    {
        var loaderIds = instance.Mods
            .Select(mod => mod.Metadata.Loader?.LoaderId)
            .OfType<string>()
            .Distinct(ModIds.Comparer)
            .Order(ModIds.Comparer)
            .ToList();

        return loaderIds.Count switch
        {
            1 => loaderIds[0],
            0 => throw new InvalidOperationException(
                $"No mod in '{instance.Name}' needs a mod loader, and the game reads no instance path on its own. To start the game without a loader, use 'borea game launch', which uses the shared profile and not this instance."),
            _ => throw new InvalidOperationException(
                $"The mods in '{instance.Name}' need different mod loaders: {string.Join(", ", loaderIds)}. Name the one to use: 'borea launch <instance> <loader-id>'."),
        };
    }
}
