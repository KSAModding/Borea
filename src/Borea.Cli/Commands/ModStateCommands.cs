using System.CommandLine;
using Borea.Core.Mods;
using Borea.Core.State;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea enable</c> and <c>borea disable</c>: whether the game loads a mod.
/// The manifest is the game's file, and the game names a mod by its folder, so a
/// folder the user made by hand is enabled and disabled like an installed one.
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

            var flipped = await cli.ModState.SetActiveAsync(target.InstanceId, id, ct).ConfigureAwait(false);

            // Nothing flipped means the manifest already lists the mod as enabled,
            // or does not list it at all. The second case writes an entry, and
            // it is also the only one that needs the files, so a mod whose folder
            // the user deleted can still be enabled through its entry.
            if (!flipped && !await cli.ModState.IsActiveAsync(target.InstanceId, id, ct).ConfigureAwait(false))
            {
                if (!ModIds.IsValid(id))
                    throw new InvalidOperationException($"'{id}' cannot name a mod folder, so no entry can be written for it.");

                var added = await cli.ModState.AddEntryAsync(target.InstanceId, id, enabled: true, ct).ConfigureAwait(false);
                if (added is ModEntryAddResult.NotOnDisk)
                    throw new InvalidOperationException($"'{target.Name}' has no mod folder named '{id}'.");
            }

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
