using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Instances;
using Borea.Core.Launch;

namespace Borea.Cli.Commands;

internal static class LaunchCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", "The instance's name, or its id when two names differ only in case.");
        var loaderId = ArgumentRules.OptionalContentId("loader-id", "The installed mod loader to use. Omit it to use the loader the mods need, or an installed loader that takes an instance when no mod needs one.");
        var json = ArgumentRules.Json();
        var launch = new Command("launch", "Start one instance through an installed mod loader.");
        launch.Arguments.Add(instance);
        launch.Arguments.Add(loaderId);
        launch.Options.Add(json);

        launch.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup
                .ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance))
                .ConfigureAwait(false);
            var givenLoaderId = parseResult.GetValue(loaderId);
            var listings = await cli.Mods.GetAvailableModsAsync(ct).ConfigureAwait(false);
            var choice = LaunchLoaderChoice.Choose(target, cli.Settings.LoaderInstallations, listings, givenLoaderId);
            if (!choice.Succeeded)
            {
                // an id that names no mod loader gets that answer, not advice to install it
                if (choice.Failure == LaunchLoaderFailure.GivenLoaderNotInstalled)
                    _ = LoaderLookup.GetListing(listings, choice.LoaderIds[0]);

                throw new InvalidOperationException(FailureMessage(target, choice));
            }

            var loader = choice.Loader;
            if (givenLoaderId is null && !parseResult.GetValue(json))
            {
                output.WriteLine(choice.RequiredByMods
                    ? $"The mods in '{target.Name}' need {loader.ModId}."
                    : $"No mod in '{target.Name}' needs a mod loader. Using {loader.ModId}, which takes an instance.");
            }

            ct.ThrowIfCancellationRequested();
            var result = cli.Launcher.Launch(target, loader);

            // a loader that stops with an error while the game loads is reported, not left to vanish
            if (result.Started)
                result = await cli.Launcher.WatchStartAsync(target, result, ct).ConfigureAwait(false);

            if (!result.Started)
            {
                error.WriteLine($"error: {result.Message}");
                if (result.Outcome == LaunchOutcome.ExitedEarly)
                {
                    error.WriteLine($"{loader.Name} wrote:");
                    foreach (var line in result.Output)
                        error.WriteLine($"  {line}");
                }

                return ExitCodes.Failed;
            }

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new LaunchView(loader.ModId, choice.RequiredByMods, result.Plan!.Executable, result.Plan.WorkingDirectory, result.ProcessId!.Value));
                return ExitCodes.Done;
            }

            output.WriteLine(result.Message);
            output.WriteLine($"Process id: {result.ProcessId}");
            return ExitCodes.Done;
        }));

        return launch;
    }

    private static string FailureMessage(Instance instance, LaunchLoaderChoice choice) => choice.Failure switch
    {
        LaunchLoaderFailure.GivenLoaderNotInstalled =>
            $"Mod loader '{choice.LoaderIds[0]}' is not installed. {InstallHint(choice.LoaderIds[0])}",
        LaunchLoaderFailure.NeededLoaderNotInstalled =>
            $"The mods in '{instance.Name}' need {choice.LoaderIds[0]}, and it is not installed. {InstallHint(choice.LoaderIds[0])}",
        LaunchLoaderFailure.DifferentLoadersNeeded =>
            $"The mods in '{instance.Name}' need different mod loaders: {string.Join(", ", choice.LoaderIds)}. Name the one to use: 'borea launch <instance> <loader-id>'.",
        LaunchLoaderFailure.LoaderNotListed =>
            $"No configured source has a listing for {string.Join(", ", choice.LoaderIds)}. Borea needs the listing of an installed mod loader to know how to start it.",
        LaunchLoaderFailure.NoLoaderTakesInstance =>
            $"No mod in '{instance.Name}' needs a mod loader, and no installed mod loader takes an instance. The game reads no instance path on its own. To start the game without a loader, use 'borea game launch', which uses the shared profile and not this instance.",
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice.Failure, null),
    };

    private static string InstallHint(string loaderId) =>
        $"Install it with 'borea loader install {loaderId}', or record an existing copy with 'borea loader adopt {loaderId} <directory>'.";

    /// <summary>The JSON shape of <c>launch</c>.</summary>
    private sealed record LaunchView(string LoaderId, bool RequiredByMods, string Executable, string WorkingDirectory, int ProcessId);
}
