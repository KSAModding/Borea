using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Settings;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea settings</c>: where the game, the mod loaders and the library are, the release channel and the shared mod store.
/// </summary>
internal static class SettingsCommand
{
    private const string On = "on";
    private const string Off = "off";

    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var settings = new Command("settings", "Read and write Borea's own settings: where the game, the mod loaders and the library are, the release channel and the shared mod store.");
        settings.Subcommands.Add(BuildShow(services));
        settings.Subcommands.Add(BuildSet(services));
        return settings;
    }

    private static Command BuildShow(Func<CancellationToken, Task<CliServices>> services)
    {
        var json = ArgumentRules.Json();
        var show = new Command("show", "Print the saved settings.");
        show.Options.Add(json);

        show.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, (cli, output, _, _) =>
        {
            if (parseResult.GetValue(json))
                JsonOutput.Write(output, SettingsView.From(cli.Settings, cli.Paths));
            else
                WriteSettings(output, cli.Settings, cli.Paths);

            return Task.FromResult(ExitCodes.Done);
        }));

        return show;
    }

    private static Command BuildSet(Func<CancellationToken, Task<CliServices>> services)
    {
        var set = new Command("set", "Change one setting. The other settings stay as they are.");
        set.Subcommands.Add(BuildSetGame(services));
        set.Subcommands.Add(BuildSetLoader(services));
        set.Subcommands.Add(BuildSetChannel(services));
        set.Subcommands.Add(BuildSetLibrary(services));
        set.Subcommands.Add(BuildSetSharedStore(services));
        return set;
    }

    private static Command BuildSetLibrary(Func<CancellationToken, Task<CliServices>> services)
    {
        var directory = new Argument<string?>("directory") { Description = "The folder for the Instances and Backups folders.", Arity = ArgumentArity.ZeroOrOne };
        var useDefault = new Option<bool>("--default") { Description = "Move the library back to Borea's own folder." };
        var json = ArgumentRules.Json();
        var library = new Command("library", "Move the instances and backups to another folder, or use the library a folder already holds.");
        library.Arguments.Add(directory);
        library.Options.Add(useDefault);
        library.Options.Add(json);
        library.Validators.Add(result =>
        {
            var given = result.GetValue(directory);
            if (result.GetValue(useDefault) == (given is not null))
                result.AddError("Give either a directory or --default.");
            else if (given is not null && string.IsNullOrWhiteSpace(given))
                result.AddError("The directory cannot be empty.");
        });

        library.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var given = parseResult.GetValue(directory);
            var folder = given is null ? null : Path.GetFullPath(given);
            var result = await cli.LibraryFolderChanger.ChangeAsync(folder, new LibraryMoveProgressOutput(error), ct).ConfigureAwait(false);
            if (!result.Changed)
            {
                error.WriteLine($"error: {result.Message}");
                return ExitCodes.Failed;
            }

            if (result.OldFilesRemain)
                error.WriteLine($"warning: Borea could not delete every old file in {result.PreviousFolder}.");

            var moved = result.Outcome == LibraryFolderChangeOutcome.Moved;
            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new LibraryChangeView(result.Folder, result.PreviousFolder, moved ? "moved" : "adopted", result.OldFilesRemain));
            }
            else
            {
                output.WriteLine($"Library folder: {result.Folder}");
                if (!moved)
                    output.WriteLine("The folder already held a library, so nothing was moved.");
            }

            return ExitCodes.Done;
        }));

        return library;
    }

    private static Command BuildSetSharedStore(Func<CancellationToken, Task<CliServices>> services)
    {
        var state = new Argument<string>("state") { Description = "on or off." };
        state.AcceptOnlyFromAmong(On, Off);
        var sharedStore = new Command("shared-store", "Store each mod release once and link every instance to it, or copy it into each instance. Off also gives every instance its own copy of the mods it links to.");
        sharedStore.Arguments.Add(state);

        sharedStore.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var enabled = parseResult.GetRequiredValue(state) == On;
            var change = await cli.SharedModStore.SetEnabledAsync(enabled, ct).ConfigureAwait(false);
            if (change != SharedModStoreChange.Saved)
            {
                error.WriteLine(change == SharedModStoreChange.GameRunning
                    ? "error: The game is running. Close the game first, then turn the shared mod store off."
                    : "error: Another Borea window or command is running. Close it first, then turn the shared mod store off.");
                return ExitCodes.Failed;
            }

            output.WriteLine($"Shared mod store: {OnOff(enabled)}");
            return ExitCodes.Done;
        }));

        return sharedStore;
    }

    private static Command BuildSetChannel(Func<CancellationToken, Task<CliServices>> services)
    {
        var name = ArgumentRules.Channel("channel", "stable, testing or dev.");
        var channel = new Command("channel", "Choose which release statuses install and update offer: stable, testing or dev. An exact version installs from any channel.");
        channel.Arguments.Add(name);

        channel.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var chosen = ArgumentRules.ChannelOrSaved(parseResult.GetRequiredValue(name), ReleaseChannel.Stable);

            // read again, so a change saved since the services were built is kept
            var current = await cli.SettingsRepository.GetAsync(ct).ConfigureAwait(false)
                ?? new BoreaSettings(gameDirectoryPath: null);
            await cli.SettingsRepository.SaveAsync(current.WithReleaseChannel(chosen), ct).ConfigureAwait(false);

            output.WriteLine($"Release channel: {chosen.ToName()}");
            return ExitCodes.Done;
        }));

        return channel;
    }

    private static Command BuildSetGame(Func<CancellationToken, Task<CliServices>> services)
    {
        var directory = ArgumentRules.Text("directory", "The folder that holds the game executable.");
        var game = new Command("game", "Point Borea at the game installation.");
        game.Arguments.Add(directory);

        game.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var fullPath = Path.GetFullPath(parseResult.GetRequiredValue(directory));
            await cli.GameDirectoryChanger.ChangeAsync(fullPath, ct).ConfigureAwait(false);

            output.WriteLine($"Game directory: {fullPath}");
            WarnWhenMissing(error, fullPath);
            return ExitCodes.Done;
        }));

        return game;
    }

    private static Command BuildSetLoader(Func<CancellationToken, Task<CliServices>> services)
    {
        var loaderId = ArgumentRules.ContentId("loader-id", "The loader's content id, such as StarMap.");
        var directory = ArgumentRules.Text("directory", "The folder the loader is installed in.");
        var loader = new Command("loader", "Point Borea at an installed mod loader.");
        loader.Arguments.Add(loaderId);
        loader.Arguments.Add(directory);

        loader.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var id = parseResult.GetRequiredValue(loaderId);
            var fullPath = Path.GetFullPath(parseResult.GetRequiredValue(directory));
            return await LoaderCommand.AdoptAsync(cli, id, fullPath, output, error, ct).ConfigureAwait(false);
        }));

        return loader;
    }

    /// <summary>
    /// The path is saved as given, because Borea can be set up before the game
    /// is installed. A typo still deserves a word.
    /// </summary>
    private static void WarnWhenMissing(TextWriter error, string directory)
    {
        if (!Directory.Exists(directory))
            error.WriteLine($"warning: {directory} does not exist.");
    }

    private static void WriteSettings(TextWriter output, BoreaSettings settings, IGamePathProvider paths)
    {
        output.WriteLine($"Game directory: {settings.GameDirectoryPath ?? "not set"}");
        output.WriteLine($"Library folder: {LibraryFolderOf(paths)}{(settings.LibraryFolderPath is null ? " (default)" : string.Empty)}");
        output.WriteLine($"Release channel: {settings.ReleaseChannel.ToName()}");
        output.WriteLine($"Shared mod store: {OnOff(settings.SharedModStore)}");

        if (settings.LoaderInstallations.Count == 0)
        {
            output.WriteLine("Loader directories: none");
            return;
        }

        output.WriteLine("Loader directories:");
        foreach (var (loaderId, installation) in settings.LoaderInstallations.OrderBy(p => p.Key, ModIds.Comparer))
            output.WriteLine($"  {loaderId}: {installation.DirectoryPath}");
    }

    private static string OnOff(bool value) => value ? On : Off;

    private static string LibraryFolderOf(IGamePathProvider paths) => Path.GetDirectoryName(paths.GetInstancesRoot())!;

    /// <summary>The JSON shape of <c>settings show</c>.</summary>
    private sealed record SettingsView(string? GameDirectory, IReadOnlyDictionary<string, string> LoaderDirectories, string ReleaseChannel, string LibraryFolder, bool LibraryFolderIsDefault, bool SharedModStore)
    {
        public static SettingsView From(BoreaSettings settings, IGamePathProvider paths)
            => new(
                settings.GameDirectoryPath,
                settings.LoaderInstallations.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.DirectoryPath,
                    ModIds.Comparer),
                settings.ReleaseChannel.ToName(),
                LibraryFolderOf(paths),
                settings.LibraryFolderPath is null,
                settings.SharedModStore);
    }

    /// <summary>The JSON shape of <c>settings set library</c>.</summary>
    private sealed record LibraryChangeView(string LibraryFolder, string PreviousLibraryFolder, string Outcome, bool OldFilesRemain);
}
