using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Instances;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea instance</c>: the lifecycle of instances.
/// </summary>
internal static class InstanceCommand
{
    private const string InstanceArgumentDescription = "The instance's name, or its id when two names differ only in case.";

    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = new Command("instance", "List, create, rename, delete, and activate instances.");
        instance.Subcommands.Add(BuildList(services));
        instance.Subcommands.Add(BuildCreate(services));
        instance.Subcommands.Add(BuildRename(services));
        instance.Subcommands.Add(BuildDelete(services));
        instance.Subcommands.Add(BuildActivate(services));
        return instance;
    }

    private static Command BuildList(Func<CancellationToken, Task<CliServices>> services)
    {
        var json = ArgumentRules.Json();
        var list = new Command("list", "Print every instance and mark the active one.");
        list.Options.Add(json);

        list.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var activeId = await cli.Instances.GetActiveInstanceIdAsync().ConfigureAwait(false);
            var instances = (await cli.Instances.GetAllAsync().ConfigureAwait(false))
                .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(instance => instance.CreatedAt)
                .ToList();

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, instances.Select(instance => InstanceView.From(instance, instance.InstanceId == activeId)));
                return ExitCodes.Done;
            }

            if (instances.Count == 0)
            {
                output.WriteLine("No instances.");
                return ExitCodes.Done;
            }

            var nameWidth = instances.Max(instance => instance.Name.Length);
            foreach (var instance in instances)
            {
                var marker = instance.InstanceId == activeId ? "*" : " ";
                output.WriteLine($"{marker} {instance.Name.PadRight(nameWidth)}  {instance.InstanceId}  {Describe(instance.Source)}");
            }

            return ExitCodes.Done;
        }));

        return list;
    }

    private static Command BuildCreate(Func<CancellationToken, Task<CliServices>> services)
    {
        var name = ArgumentRules.Text("name", "The display name. Names compare case-insensitively.");
        var create = new Command("create", "Create an empty instance.");
        create.Arguments.Add(name);

        create.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var created = await cli.Instances.CreateAsync(parseResult.GetRequiredValue(name), InstanceSource.Custom.Value).ConfigureAwait(false);

            output.WriteLine($"Created instance '{created.Name}' ({created.InstanceId}).");
            return ExitCodes.Done;
        }));

        return create;
    }

    private static Command BuildRename(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var newName = ArgumentRules.Text("new-name", "The new display name.");
        var rename = new Command("rename", "Give an instance a new name.");
        rename.Arguments.Add(instance);
        rename.Arguments.Add(newName);

        rename.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var renamed = parseResult.GetRequiredValue(newName);
            await cli.Instances.RenameAsync(target.InstanceId, renamed).ConfigureAwait(false);

            output.WriteLine($"Renamed '{target.Name}' to '{renamed}'.");
            return ExitCodes.Done;
        }));

        return rename;
    }

    private static Command BuildDelete(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var delete = new Command("delete", "Delete an instance and everything in its folder.");
        delete.Arguments.Add(instance);

        delete.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            await cli.Instances.DeleteAsync(target.InstanceId).ConfigureAwait(false);

            output.WriteLine($"Deleted instance '{target.Name}' ({target.InstanceId}).");
            return ExitCodes.Done;
        }));

        return delete;
    }

    private static Command BuildActivate(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var activate = new Command("activate", "Make an instance the active one. It is the instance that launches, and the one 'enable' and 'disable' act on when --instance is absent.");
        activate.Arguments.Add(instance);

        activate.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            await cli.Instances.SetActiveInstanceAsync(target.InstanceId).ConfigureAwait(false);

            output.WriteLine($"Active instance: '{target.Name}' ({target.InstanceId}).");
            return ExitCodes.Done;
        }));

        return activate;
    }

    private static string Describe(InstanceSource source) => source switch
    {
        InstanceSource.FromModPack pack => $"modpack {pack.ModPackId} {pack.Version}",
        _ => "custom",
    };

    /// <summary>One entry of <c>instance list --json</c>.</summary>
    private sealed record InstanceView(Guid Id, string Name, bool Active, InstanceSourceView Source, DateTimeOffset CreatedAt)
    {
        public static InstanceView From(Instance instance, bool active)
            => new(instance.InstanceId, instance.Name, active, InstanceSourceView.From(instance.Source), instance.CreatedAt);
    }

    private sealed record InstanceSourceView(string Kind, string? ModPackId, string? Version)
    {
        public static InstanceSourceView From(InstanceSource source) => source switch
        {
            InstanceSource.FromModPack pack => new("modpack", pack.ModPackId, pack.Version.ToString()),
            _ => new("custom", null, null),
        };
    }
}
