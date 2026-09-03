using System.CommandLine;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea enable</c> and <c>borea disable</c>: whether the game loads a mod.
/// The manifest is the game's file, and the game names a mod by its folder, so
/// the id is any text and not a content id: a folder the user made by hand is
/// enabled and disabled like an installed one.
/// </summary>
internal static class ModStateCommands
{
    private const string ModIdDescription = "The mod's id, the name of its folder.";

    public static Command BuildEnable(Func<CancellationToken, Task<CliServices>> services)
    {
        var modId = ArgumentRules.Text("mod-id", ModIdDescription);
        var instance = ArgumentRules.Instance();
        var enable = new Command("enable", "Make the game load a mod in an instance.");
        enable.Arguments.Add(modId);
        enable.Options.Add(instance);

        enable.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var id = parseResult.GetRequiredValue(modId);
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parseResult.GetValue(instance)).ConfigureAwait(false);

            // The repository does not say whether the entry changed, so the
            // message states the end state.
            await cli.ModState.SetActiveAsync(target.InstanceId, id, ct).ConfigureAwait(false);

            output.WriteLine($"Enabled {id} in '{target.Name}'.");
            return ExitCodes.Done;
        }));

        return enable;
    }

    public static Command BuildDisable(Func<CancellationToken, Task<CliServices>> services)
    {
        var modId = ArgumentRules.Text("mod-id", ModIdDescription);
        var instance = ArgumentRules.Instance();
        var disable = new Command("disable", "Stop the game from loading a mod in an instance. The files stay.");
        disable.Arguments.Add(modId);
        disable.Options.Add(instance);

        disable.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var id = parseResult.GetRequiredValue(modId);
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parseResult.GetValue(instance)).ConfigureAwait(false);
            await cli.ModState.SetInactiveAsync(target.InstanceId, id, ct).ConfigureAwait(false);

            output.WriteLine($"Disabled {id} in '{target.Name}'.");
            return ExitCodes.Done;
        }));

        return disable;
    }
}
