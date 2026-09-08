using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea settings</c>: where the game and the mod loaders are.
/// </summary>
internal static class SettingsCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var settings = new Command("settings", "Read and write Borea's own settings: where the game and the mod loaders are.");
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
                JsonOutput.Write(output, SettingsView.From(cli.Settings));
            else
                WriteSettings(output, cli.Settings);

            return Task.FromResult(ExitCodes.Done);
        }));

        return show;
    }

    private static Command BuildSet(Func<CancellationToken, Task<CliServices>> services)
    {
        var set = new Command("set", "Change one setting. The other settings stay as they are.");
        set.Subcommands.Add(BuildSetGame(services));
        set.Subcommands.Add(BuildSetLoader(services));
        return set;
    }

    private static Command BuildSetGame(Func<CancellationToken, Task<CliServices>> services)
    {
        var directory = ArgumentRules.Text("directory", "The folder that holds the game executable.");
        var game = new Command("game", "Point Borea at the game installation.");
        game.Arguments.Add(directory);

        game.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var fullPath = Path.GetFullPath(parseResult.GetRequiredValue(directory));
            await cli.SettingsRepository.SaveAsync(cli.Settings.WithGameDirectory(fullPath), ct).ConfigureAwait(false);

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
            await cli.SettingsRepository.SaveAsync(cli.Settings.WithLoaderDirectory(id, fullPath), ct).ConfigureAwait(false);

            output.WriteLine($"Loader {id} directory: {fullPath}");
            WarnWhenMissing(error, fullPath);
            return ExitCodes.Done;
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

    private static void WriteSettings(TextWriter output, BoreaSettings settings)
    {
        output.WriteLine($"Game directory: {settings.GameDirectoryPath ?? "not set"}");

        if (settings.LoaderDirectoryPaths.Count == 0)
        {
            output.WriteLine("Loader directories: none");
            return;
        }

        output.WriteLine("Loader directories:");
        foreach (var (loaderId, path) in settings.LoaderDirectoryPaths.OrderBy(p => p.Key, ModIds.Comparer))
            output.WriteLine($"  {loaderId}: {path}");
    }

    /// <summary>The JSON shape of <c>settings show</c>.</summary>
    private sealed record SettingsView(string? GameDirectory, IReadOnlyDictionary<string, string> LoaderDirectories)
    {
        public static SettingsView From(BoreaSettings settings)
            => new(settings.GameDirectoryPath, settings.LoaderDirectoryPaths);
    }
}
