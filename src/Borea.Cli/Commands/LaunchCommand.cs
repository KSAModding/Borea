using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

internal static class LaunchCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services, PassThroughArguments passThrough)
    {
        var instance = ArgumentRules.Text("instance", InstanceCommand.InstanceArgumentDescription);
        var loaderId = ArgumentRules.OptionalContentId("loader-id", "The installed mod loader to use. Omit it to use the loader the mods need, or an installed loader that takes an instance when no mod needs one.");
        var json = ArgumentRules.Json();
        var launch = new Command("launch", "Start one instance through an installed mod loader. Arguments after -- go to the loader and the game for this launch, after the saved launch arguments of the instance.");
        launch.Arguments.Add(instance);
        launch.Arguments.Add(loaderId);
        launch.Options.Add(json);
        passThrough.Accept(launch);

        launch.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, passThrough, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup
                .ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance))
                .ConfigureAwait(false);
            var givenLoaderId = parseResult.GetValue(loaderId);
            var game = cli.InstanceGames.Resolve(target);

            // a kept build that is not there has no version to rate a loader against
            if (game is { Problem: not KeptBuildProblem.None, KeptBuild: { } missing })
                throw new InvalidOperationException(LaunchResult.GameBuildMissing(target, cli.Paths.GetGameBuildFolder(missing)).Message);

            var installed = await cli.LoaderVersions.ListAsync(ct).ConfigureAwait(false);
            var resolution = await LoaderLookup.ResolveAsync(cli, target, installed, game.Version?.Version, givenLoaderId, ct).ConfigureAwait(false);
            var choice = resolution.Choice;
            if (resolution.Start is not { } start)
            {
                // an id that names no mod loader gets that answer, not advice to install it
                if (choice.Failure == LaunchLoaderFailure.GivenLoaderNotInstalled)
                    _ = LoaderLookup.GetListing(resolution.Listings, choice.LoaderIds[0]);

                throw new InvalidOperationException(MissingMessage(target, resolution) ?? FailureMessage(target, choice));
            }

            var loader = choice.Loader!;
            var version = resolution.Version!;
            if (givenLoaderId is null && !parseResult.GetValue(json))
            {
                output.WriteLine(choice.RequiredByMods
                    ? $"The mods in '{target.Name}' need {loader.ModId}."
                    : $"No mod in '{target.Name}' needs a mod loader. Using {loader.ModId}, which takes an instance.");
            }

            WriteLoaderWarning(loader, version, game, error);

            var shape = await cli.GameShape.GetForInstanceAsync(target.InstanceId, ct).ConfigureAwait(false);
            GameShapeOutput.WriteWarning(shape, error);

            // The load order is a write, so it waits for a shape Borea trusts. The folder is the one the instance starts,
            // the same installation the shape check read.
            if (shape.AllowsWrites
                && game.GameDirectory is { } gameDirectory
                && await cli.ModState.PutGameContentFirstAsync(target.InstanceId, gameDirectory, ct).ConfigureAwait(false))
            {
                cli.Log.Write($"Instance {target.InstanceId}: the game's own content now loads before the mods.");
            }

            ct.ThrowIfCancellationRequested();
            var result = cli.Launcher.Launch(target, loader, start.DirectoryPath, passThrough.Values, start.IsAdopted);

            // a loader that stops with an error while the game loads is reported, not left to vanish
            if (result.Started)
                result = await cli.Launcher.WatchStartAsync(target, result, ct).ConfigureAwait(false);

            if (!result.Started)
            {
                error.WriteLine($"error: {result.Message}");
                if (result.CrashCause == LoaderCrashCause.GameAssembly)
                    error.WriteLine(GameAssemblyHint(target, loader, version, game));

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
                JsonOutput.Write(output, new LaunchView(loader.ModId, start.Version?.ToString(), choice.RequiredByMods, result.Plan!.Executable, result.Plan.Arguments, result.Plan.WorkingDirectory, result.ProcessId!.Value));
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

    /// <summary>
    /// Why no installed version starts the instance when the resolver names the missing version, with the command
    /// that installs it. Null when the failure of the loader choice says more.
    /// </summary>
    private static string? MissingMessage(Instance instance, InstanceLoaderResolution resolution)
    {
        if (resolution.Version is not { } version)
            return null;

        var id = version.LoaderId;
        if (version.IsPinned && instance.Loader.Version is { } pin)
        {
            return version.Problem == LoaderVersionProblem.NotInstallable
                ? $"Instance '{instance.Name}' is set to {id} {pin}, which is not installed, and the index does not offer it. Let Borea choose the version that fits the game with 'borea instance set-loader \"{instance.Name}\" auto'."
                : $"Instance '{instance.Name}' is set to {id} {pin}, which is not installed. Install it with 'borea loader install {id} --version {pin}'.";
        }

        return version.Missing is { } missing
            ? $"Instance '{instance.Name}' needs {id} {missing.Version}, and it is not installed. Install it with 'borea loader install {id} --version {missing.Version}'."
            : null;
    }

    /// <summary>
    /// What to do when the loader cannot load the game. An instance that is set to one version keeps it after an
    /// install, so it also gets the command that lets Borea choose the version again.
    /// </summary>
    private static string GameAssemblyHint(Instance instance, ModMetadata loader, LoaderVersionResolution version, InstanceGame game)
    {
        var install = InstallCommand(loader, game, version.Offer?.Version.ToString() ?? "<version>");
        return version.IsPinned
            ? $"Instance '{instance.Name}' is set to {loader.Name} {version.Start?.Version}. Install a {loader.Name} release for this KSA build with '{install}', then let Borea choose it with 'borea instance set-loader \"{instance.Name}\" auto'."
            : $"Install a {loader.Name} release for this KSA build with '{install}'. The instance then starts the newest installed release that fits the game.";
    }

    /// <summary>
    /// Warns when the index says the loader version that starts the instance does not support the game. The launch
    /// still starts, because the bounds can be wrong. An instance that is set to one version gets no install advice,
    /// because another installed version does not change what it starts.
    /// </summary>
    private static void WriteLoaderWarning(ModMetadata loader, LoaderVersionResolution version, InstanceGame game, TextWriter error)
    {
        if (version.StartCompatibility != GameCompatibility.Incompatible || version.Start?.Version is not { } started)
            return;

        error.WriteLine(version.Offer is { } offer
            ? $"warning: {loader.Name} {started} does not support KSA {game.Version?.Version}. Run '{InstallCommand(loader, game, offer.Version.ToString())}' to install {offer.Version}."
            : $"warning: {loader.Name} {started} does not support KSA {game.Version?.Version}.");
    }

    /// <summary>
    /// The command that installs a loader release for the game of an instance. Without a version, the install picks the
    /// release for the build of the settings, so an instance on a kept build gets <paramref name="version"/>.
    /// </summary>
    private static string InstallCommand(ModMetadata loader, InstanceGame game, string version) =>
        game.KeptBuild is null ? $"borea loader install {loader.ModId}" : $"borea loader install {loader.ModId} --version {version}";

    private static string InstallHint(string loaderId) =>
        $"Install it with 'borea loader install {loaderId}', or record an existing copy with 'borea loader adopt {loaderId} <directory>'.";

    /// <summary>The JSON shape of <c>launch</c>.</summary>
    private sealed record LaunchView(string LoaderId, string? LoaderVersion, bool RequiredByMods, string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory, int ProcessId);
}
