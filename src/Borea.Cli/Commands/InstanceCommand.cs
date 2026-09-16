using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea instance</c>: the lifecycle of instances.
/// </summary>
internal static class InstanceCommand
{
    private const string InstanceArgumentDescription = "The instance's name, or its id when two names differ only in case.";

    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = new Command("instance", "List, create, rename, delete, activate, and deactivate instances, and adopt mods that Borea did not install.");
        instance.Subcommands.Add(BuildList(services));
        instance.Subcommands.Add(BuildCreate(services));
        instance.Subcommands.Add(BuildRename(services));
        instance.Subcommands.Add(BuildDelete(services));
        instance.Subcommands.Add(BuildActivate(services));
        instance.Subcommands.Add(BuildDeactivate(services));
        instance.Subcommands.Add(BuildMods(services));
        instance.Subcommands.Add(BuildScan(services));
        instance.Subcommands.Add(BuildAdopt(services));
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

    private static Command BuildDeactivate(Func<CancellationToken, Task<CliServices>> services)
    {
        var json = ArgumentRules.Json();
        var deactivate = new Command("deactivate", "Leave no instance active. Borea deletes nothing, and 'enable' and 'disable' then need --instance.");
        deactivate.Options.Add(json);

        deactivate.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var activeId = await cli.Instances.GetActiveInstanceIdAsync().ConfigureAwait(false);
            var previous = activeId is { } id ? await cli.Instances.GetByIdAsync(id).ConfigureAwait(false) : null;
            await cli.Instances.ClearActiveInstanceAsync().ConfigureAwait(false);

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new DeactivateView(activeId is not null, activeId, previous?.Name));
                return ExitCodes.Done;
            }

            output.WriteLine(activeId switch
            {
                null => "No instance was active.",
                _ when previous is not null => $"No instance is active now. '{previous.Name}' ({activeId}) was the active instance.",
                _ => $"No instance is active now. The active instance was {activeId}, which does not exist any more.",
            });
            return ExitCodes.Done;
        }));

        return deactivate;
    }

    private static Command BuildMods(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var json = ArgumentRules.Json();
        var mods = new Command("mods", "Print the manifest entries in load order.");
        mods.Arguments.Add(instance);
        mods.Options.Add(json);

        mods.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var entries = await cli.ModState.GetEntriesAsync(target.InstanceId, ct).ConfigureAwait(false);

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, entries.Select(entry => new ModView(entry.ModId, entry.Enabled)));
                return ExitCodes.Done;
            }

            if (entries.Count == 0)
            {
                output.WriteLine($"No mods in '{target.Name}'.");
                return ExitCodes.Done;
            }

            foreach (var entry in entries)
                output.WriteLine($"{(entry.Enabled ? "enabled " : "disabled")}  {entry.ModId}");

            return ExitCodes.Done;
        }));

        return mods;
    }

    private static Command BuildScan(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var json = ArgumentRules.Json();
        var scan = new Command("scan", "Print the mod folders that Borea did not install, and whether the content index lists them.");
        scan.Arguments.Add(instance);
        scan.Options.Add(json);

        scan.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var foreignMods = await cli.ForeignModAdopter.ScanAsync(target.InstanceId, ct).ConfigureAwait(false);
            ContentIndexSnapshot? snapshot = null;
            if (foreignMods.Count > 0)
            {
                try
                {
                    snapshot = await cli.IndexSnapshots.GetSnapshotAsync(ct).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException)
                {
                    error.WriteLine($"warning: The content index could not be read. {exception.Message}");
                }
            }

            var views = foreignMods.Select(mod => ForeignModView.From(mod, snapshot)).ToList();

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, views);
                return ExitCodes.Done;
            }

            if (views.Count == 0)
            {
                output.WriteLine($"No mod folders in '{target.Name}' that Borea did not install.");
                return ExitCodes.Done;
            }

            var folderWidth = views.Max(view => view.Folder.Length);
            foreach (var view in views)
            {
                output.WriteLine($"{view.Folder.PadRight(folderWidth)}  {DescribeIndexState(view.InIndex)}");
                if (view.DependencyReadError is not null)
                    error.WriteLine($"warning: The mod.toml of '{view.Folder}' could not be read. {view.DependencyReadError}");
            }

            return ExitCodes.Done;
        }));

        return scan;
    }

    private static Command BuildAdopt(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var folder = ArgumentRules.Text("folder", "The name of the mod folder in the instance.");
        folder.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is "." or ".."
                || value.Contains(Path.DirectorySeparatorChar)
                || value.Contains(Path.AltDirectorySeparatorChar))
                result.AddError($"'{value}' is not a folder name.");
        });
        var archive = new Option<string>("--archive")
        {
            Description = "The archive the folder was installed from. Its SHA-256 must match a release in the content index.",
            Required = true,
        };
        archive.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                result.AddError("The --archive value cannot be empty.");
        });
        var adopt = new Command("adopt", "Record a mod folder that Borea did not install as the index release its archive matches. Borea does not delete or change the folder.");
        adopt.Arguments.Add(instance);
        adopt.Arguments.Add(folder);
        adopt.Options.Add(archive);

        adopt.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var folderName = parseResult.GetRequiredValue(folder);
            var result = await cli.ForeignModAdopter
                .AdoptArchiveAsync(target.InstanceId, folderName, Path.GetFullPath(parseResult.GetRequiredValue(archive)), ct)
                .ConfigureAwait(false);

            if (result.InstalledMod is not { } adopted)
            {
                error.WriteLine($"error: The archive (SHA-256 {result.Sha256}) matches no release of '{folderName}' in the content index. The folder stays as it is.");
                return ExitCodes.Failed;
            }

            output.WriteLine($"Adopted '{adopted.ModId}' {adopted.Version} in '{target.Name}'.");
            return ExitCodes.Done;
        }));

        return adopt;
    }

    private static string DescribeIndexState(bool? inIndex) => inIndex switch
    {
        true => "in the content index",
        false => "not in the content index",
        null => "content index not available",
    };

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

    /// <summary>The JSON shape of <c>instance deactivate</c>. The id and name are of the instance that was active, and null when none was.</summary>
    private sealed record DeactivateView(bool Deactivated, Guid? Id, string? Name);

    private sealed record ModView(string Id, bool Enabled);

    /// <summary>One entry of <c>instance scan --json</c>.</summary>
    private sealed record ForeignModView(string Folder, bool? InIndex, IReadOnlyList<DependencyView> Dependencies, string? DependencyReadError)
    {
        public static ForeignModView From(ForeignMod mod, ContentIndexSnapshot? snapshot)
            => new(
                mod.FolderName,
                snapshot?.Listings.Any(listing => ModIds.Equals(listing.Id, mod.FolderName) && listing.Authored?.Type == ContentType.Mod),
                mod.Dependencies.Select(dependency => new DependencyView(dependency.ModId, dependency.Optional)).ToList(),
                mod.DependencyReadError);
    }

    private sealed record DependencyView(string Id, bool Optional);
}
