using System.CommandLine;
using System.Globalization;
using Borea.Cli.Output;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea instance</c>: the lifecycle of instances.
/// </summary>
internal static class InstanceCommand
{
    internal const string InstanceArgumentDescription = "The instance's name, or its id when two names differ only in case.";

    public static Command Build(Func<CancellationToken, Task<CliServices>> services, PassThroughArguments passThrough)
    {
        var instance = new Command("instance", "List, show, create, duplicate, rename, delete, activate, and deactivate instances, set their launch arguments, create one from the mods of the shared profile, export and import modlists, adopt mods that Borea did not install, and restore or delete backups of saves and vehicles.");
        instance.Subcommands.Add(BuildList(services));
        instance.Subcommands.Add(BuildShow(services));
        instance.Subcommands.Add(BuildCreate(services));
        instance.Subcommands.Add(ModListCommands.BuildDuplicate(services));
        instance.Subcommands.Add(ModListCommands.BuildExport(services));
        instance.Subcommands.Add(ModListCommands.BuildImport(services));
        instance.Subcommands.Add(BuildRename(services));
        instance.Subcommands.Add(BuildArguments(services));
        instance.Subcommands.Add(BuildSetArguments(services, passThrough));
        instance.Subcommands.Add(BuildClearArguments(services));
        instance.Subcommands.Add(BuildDelete(services));
        instance.Subcommands.Add(BuildActivate(services));
        instance.Subcommands.Add(BuildDeactivate(services));
        instance.Subcommands.Add(BuildMods(services));
        instance.Subcommands.Add(BuildScan(services));
        instance.Subcommands.Add(BuildAdopt(services));
        instance.Subcommands.Add(BuildImportProfile(services));
        instance.Subcommands.Add(BackupCommands.BuildList(services));
        instance.Subcommands.Add(BackupCommands.BuildRestore(services));
        instance.Subcommands.Add(BackupCommands.BuildDelete(services));
        return instance;
    }

    private static Command BuildList(Func<CancellationToken, Task<CliServices>> services)
    {
        var json = ArgumentRules.Json();
        var list = new Command("list", "Print every instance and mark the active one.");
        list.Options.Add(json);

        list.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var activeId = await cli.Instances.GetActiveInstanceIdAsync().ConfigureAwait(false);
            var instances = (await cli.Instances.GetAllAsync().ConfigureAwait(false))
                .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(instance => instance.CreatedAt)
                .ToList();
            var views = new List<InstanceView>();
            foreach (var instance in instances)
                views.Add(InstanceView.From(instance, instance.InstanceId == activeId, await LastPlayedAsync(cli, instance, ct).ConfigureAwait(false)));

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, views);
                return ExitCodes.Done;
            }

            if (views.Count == 0)
            {
                output.WriteLine("No instances.");
                return ExitCodes.Done;
            }

            var nameWidth = views.Max(view => view.Name.Length);
            var sourceWidth = instances.Max(instance => Describe(instance.Source).Length);
            foreach (var (instance, view) in instances.Zip(views))
            {
                var marker = view.Active ? "*" : " ";
                output.WriteLine($"{marker} {view.Name.PadRight(nameWidth)}  {view.Id}  {Describe(instance.Source).PadRight(sourceWidth)}  {DescribeLastPlayed(view.LastPlayedAt)}");
            }

            return ExitCodes.Done;
        }));

        return list;
    }

    private static Command BuildShow(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var json = ArgumentRules.Json();
        var show = new Command("show", "Print one instance, when it was last played, and how long it was played.");
        show.Arguments.Add(instance);
        show.Options.Add(json);

        show.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var activeId = await cli.Instances.GetActiveInstanceIdAsync().ConfigureAwait(false);
            var lastPlayedAt = await LastPlayedAsync(cli, target, ct).ConfigureAwait(false);
            var playtime = await cli.Playtime.GetPlaytimeAsync(target.InstanceId, ct).ConfigureAwait(false);
            var view = InstanceDetailsView.From(target, target.InstanceId == activeId, lastPlayedAt, playtime);

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, view);
                return ExitCodes.Done;
            }

            output.WriteLine($"{view.Name} ({view.Id})");
            output.WriteLine($"Active: {(view.Active ? "yes" : "no")}");
            output.WriteLine($"Source: {Describe(target.Source)}");
            output.WriteLine($"Created: {Timestamp(view.CreatedAt)}");
            output.WriteLine($"Mods: {view.ModCount}");
            output.WriteLine($"Launch arguments: {DescribeLaunchArguments(target)}");
            output.WriteLine($"Last played: {(view.LastPlayedAt is { } lastPlayed ? Timestamp(lastPlayed) : "never")}");
            output.WriteLine($"Playtime: {DescribePlaytime(playtime)}");
            output.WriteLine($"Sessions: {playtime.Sessions}");
            return ExitCodes.Done;
        }));

        return show;
    }

    private static async Task<DateTimeOffset?> LastPlayedAsync(CliServices cli, Instance instance, CancellationToken cancellationToken)
        => instance.LastPlayedWith(await cli.GameLog.GetLastWriteAsync(instance.InstanceId, cancellationToken).ConfigureAwait(false));

    private static string DescribeLastPlayed(DateTimeOffset? lastPlayed)
        => lastPlayed is { } at ? $"last played {Timestamp(at)}" : "never played";

    private static string DescribePlaytime(InstancePlaytime playtime)
    {
        if (!playtime.IsKnown)
            return "unknown, the newest game log could not be read";

        var total = playtime.Total.TotalHours >= 1
            ? $"{(int)playtime.Total.TotalHours} h {playtime.Total.Minutes} min"
            : $"{(int)playtime.Total.TotalMinutes} min";
        return playtime.IncludesRunningSession ? $"{total}, including the current session" : total;
    }

    private static string Timestamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string DescribeLaunchArguments(Instance instance)
        => instance.LaunchArguments.Count == 0 ? "none" : ArgumentLine.Join(instance.LaunchArguments);

    private static Command BuildCreate(Func<CancellationToken, Task<CliServices>> services)
    {
        var name = ArgumentRules.Text("name", "The display name. Names compare case-insensitively.");
        var json = ArgumentRules.Json();
        var create = new Command("create", "Create an empty instance.");
        create.Arguments.Add(name);
        create.Options.Add(json);

        create.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var created = await cli.Instances.CreateAsync(parseResult.GetRequiredValue(name), InstanceSource.Custom.Value).ConfigureAwait(false);

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new CreateView(created.Instance.InstanceId, created.Instance.Name, created.Activated));
                return ExitCodes.Done;
            }

            output.WriteLine(DescribeCreated(created));
            return ExitCodes.Done;
        }));

        return create;
    }

    internal static string DescribeCreated(InstanceCreateResult created)
        => $"Created instance '{created.Instance.Name}' ({created.Instance.InstanceId}).{NowActive(created.Activated)}";

    private static string NowActive(bool activated) => activated ? " It is now the active instance." : string.Empty;

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

    private static Command BuildArguments(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var json = ArgumentRules.Json();
        var arguments = new Command("arguments", "Print the launch arguments that every launch of an instance passes to the loader and the game.");
        arguments.Arguments.Add(instance);
        arguments.Options.Add(json);

        arguments.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, _) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            if (parseResult.GetValue(json))
                JsonOutput.Write(output, LaunchArgumentsView.From(target));
            else
                output.WriteLine(target.LaunchArguments.Count == 0 ? $"No launch arguments for '{target.Name}'." : ArgumentLine.Join(target.LaunchArguments));

            return ExitCodes.Done;
        }));

        return arguments;
    }

    private static Command BuildSetArguments(Func<CancellationToken, Task<CliServices>> services, PassThroughArguments passThrough)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var json = ArgumentRules.Json();
        var set = new Command("set-arguments", "Save the arguments after -- as the launch arguments of an instance, in place of the saved ones.");
        set.Arguments.Add(instance);
        set.Options.Add(json);
        passThrough.Accept(set);

        set.SetAction((parseResult, cancellationToken) =>
        {
            if (passThrough.Values.Count == 0)
            {
                parseResult.InvocationConfiguration.Error.WriteLine("Put the launch arguments after --, for example 'borea instance set-arguments <instance> -- -Name value'. To remove them, use 'borea instance clear-arguments <instance>'.");
                return Task.FromResult(ExitCodes.Usage);
            }

            return CommandRunner.RunAsync(parseResult, passThrough, services, cancellationToken, async (cli, output, error, ct) =>
            {
                var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
                await RefuseHandoverFlagAsync(cli, passThrough.Values, error, ct).ConfigureAwait(false);
                var saved = await SaveLaunchArgumentsAsync(cli, target, passThrough.Values, ct).ConfigureAwait(false);

                if (parseResult.GetValue(json))
                    JsonOutput.Write(output, LaunchArgumentsView.From(saved));
                else
                    output.WriteLine($"Saved the launch arguments of '{saved.Name}': {ArgumentLine.Join(saved.LaunchArguments)}");

                return ExitCodes.Done;
            });
        });

        return set;
    }

    private static Command BuildClearArguments(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var json = ArgumentRules.Json();
        var clear = new Command("clear-arguments", "Remove the launch arguments of an instance.");
        clear.Arguments.Add(instance);
        clear.Options.Add(json);

        clear.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var saved = await SaveLaunchArgumentsAsync(cli, target, [], ct).ConfigureAwait(false);

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, LaunchArgumentsView.From(saved));
            else
                output.WriteLine($"Removed the launch arguments of '{saved.Name}'.");

            return ExitCodes.Done;
        }));

        return clear;
    }

    private static Task<Instance> SaveLaunchArgumentsAsync(CliServices cli, Instance target, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => cli.Instances.UpdateAsync(target.InstanceId, saved =>
        {
            saved.SetLaunchArguments(arguments);
            return saved;
        }, cancellationToken);

    /// <summary>
    /// Refuses an argument that the listing of a configured mod loader reads as
    /// its instance flag, from the cached index. The launch checks the listing
    /// it uses again, so a listing that cannot be read now only gives a warning.
    /// </summary>
    private static async Task RefuseHandoverFlagAsync(CliServices cli, IReadOnlyList<string> arguments, TextWriter error, CancellationToken cancellationToken)
    {
        foreach (var loaderId in cli.Settings.LoaderInstallations.Keys)
        {
            ModMetadata? listing;
            try
            {
                listing = await cli.ReadOnlyMods.GetListingAsync(loaderId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                error.WriteLine($"warning: The listing of {loaderId} could not be read, so Borea checks the arguments when the instance launches. {exception.Message}");
                continue;
            }

            if (listing is { Type: ContentType.ModLoader, Provides.Instance: { } handover } && handover.FlagIn(arguments) is { } flag)
                throw new InvalidOperationException($"{listing.Name} takes the instance folder after '{flag}', and Borea passes that on every launch. A second one could make {listing.Name} use another folder, so the launch arguments were not saved.");
        }
    }

    private static Command BuildDelete(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceArgumentDescription);
        var delete = new Command("delete", "Delete an instance, everything in its folder, and the backups of its saves and vehicles.");
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
        var scan = new Command("scan", "Print the mod folders that Borea did not install, and the recorded mods whose folder is gone.");
        scan.Arguments.Add(instance);
        scan.Options.Add(json);

        scan.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var foreignMods = await cli.ForeignModAdopter.ScanAsync(target.InstanceId, ct).ConfigureAwait(false);
            var missingIds = await cli.MissingMods.ScanAsync(target.InstanceId, ct).ConfigureAwait(false);
            var snapshot = foreignMods.Count > 0 || missingIds.Count > 0 ? await ReadIndexOrWarnAsync(cli, error, ct).ConfigureAwait(false) : null;
            var views = foreignMods.Select(mod => ForeignModView.From(mod, snapshot)).ToList();
            var missing = missingIds
                .Select(id => target.Mods.First(mod => ModIds.Equals(mod.ModId, id)))
                .Select(mod => new MissingModView(mod.ModId, mod.Version.ToString(), IsListedMod(snapshot, mod.ModId)))
                .ToList();

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new ScanView(views, missing));
                return ExitCodes.Done;
            }

            if (views.Count == 0)
            {
                output.WriteLine($"No mod folders in '{target.Name}' that Borea did not install.");
            }
            else
            {
                var folderWidth = views.Max(view => view.Folder.Length);
                foreach (var view in views)
                {
                    output.WriteLine($"{view.Folder.PadRight(folderWidth)}  {DescribeIndexState(view.InIndex)}");
                    if (view.DependencyReadError is not null)
                        error.WriteLine($"warning: The mod.toml of '{view.Folder}' could not be read. {view.DependencyReadError}");
                }
            }

            if (missing.Count == 0)
            {
                output.WriteLine($"Every mod recorded in '{target.Name}' is on disk.");
                return ExitCodes.Done;
            }

            output.WriteLine($"{ModCount(missing.Count)} recorded in '{target.Name}' but not on disk:");
            var idWidth = missing.Max(view => view.Id.Length);
            foreach (var view in missing)
                output.WriteLine($"{view.Id.PadRight(idWidth)}  {view.Version}  {DescribeIndexState(view.InIndex)}");

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

    private static Command BuildImportProfile(Func<CancellationToken, Task<CliServices>> services)
    {
        var name = ArgumentRules.Text("name", "The display name of the new instance. Names compare case-insensitively.");
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the mods the import would copy, and change nothing." };
        var json = ArgumentRules.Json();
        var import = new Command(
            "import-profile",
            "Create an instance from copies of the mods in the shared profile in My Games/Kitten Space Agency, with the same load order and enabled state. A copy whose files match an index release is recorded as that release, and every other copy is a manual install. The shared profile stays as it is.");
        import.Arguments.Add(name);
        import.Options.Add(dryRun);
        import.Options.Add(json);

        import.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var instanceName = parseResult.GetRequiredValue(name);
            if (parseResult.GetValue(dryRun))
                return await PreviewImportAsync(cli, instanceName, parseResult.GetValue(json), output, error, ct).ConfigureAwait(false);

            var result = await cli.SharedProfileImporter.ImportAsync(instanceName, ct).ConfigureAwait(false);
            var views = result.Mods.Select(ImportedModView.From).ToList();
            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new ImportView(result.Instance.InstanceId, result.Instance.Name, result.Activated, views));
            }
            else
            {
                output.WriteLine($"Created instance '{result.Instance.Name}' ({result.Instance.InstanceId}) with {ModCount(views.Count)} from the shared profile.{NowActive(result.Activated)}");
                var folderWidth = views.Max(view => view.Folder.Length);
                foreach (var view in views)
                    output.WriteLine($"{(view.Enabled ? "enabled " : "disabled")}  {view.Folder.PadRight(folderWidth)}  {(view.Version is null ? "manual install" : $"release {view.Version}")}");
            }

            foreach (var mod in result.Mods.Where(mod => !mod.HasManifestEntry))
                error.WriteLine($"warning: '{mod.FolderName}' is not a valid content id, so Borea wrote no manifest entry for it. The game adds it disabled on its next start.");

            foreach (var failed in result.Mods.Where(mod => mod.MatchError is not null).GroupBy(mod => mod.MatchError))
            {
                var folders = failed.Select(mod => $"'{mod.FolderName}'").ToList();
                var outcome = folders.Count == 1 ? "it stays a manual install" : "they stay manual installs";
                error.WriteLine($"warning: Borea could not check {string.Join(", ", folders)} against the content index, so {outcome}. {failed.Key}");
            }

            return ExitCodes.Done;
        }));

        return import;
    }

    private static async Task<int> PreviewImportAsync(CliServices cli, string instanceName, bool json, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var mods = await cli.SharedProfileImporter.GetModsAsync(cancellationToken).ConfigureAwait(false);
        if (mods.Count == 0)
            throw new InvalidOperationException("The shared profile has no mods to import.");

        if (!await cli.Instances.IsNameAvailableAsync(instanceName).ConfigureAwait(false))
            throw new InvalidOperationException($"Instance name '{instanceName}' is already in use.");

        var snapshot = await ReadIndexOrWarnAsync(cli, error, cancellationToken).ConfigureAwait(false);
        var views = mods.Select(mod => new ProfileModView(mod.FolderName, mod.Enabled, IsListedMod(snapshot, mod.FolderName))).ToList();
        if (json)
        {
            JsonOutput.Write(output, new ImportPreviewView(instanceName, views));
            return ExitCodes.Done;
        }

        output.WriteLine($"Would create instance '{instanceName}' with {ModCount(views.Count)} from the shared profile. Nothing was changed.");
        var folderWidth = views.Max(view => view.Folder.Length);
        foreach (var view in views)
            output.WriteLine($"{(view.Enabled ? "enabled " : "disabled")}  {view.Folder.PadRight(folderWidth)}  {DescribeIndexState(view.InIndex)}");

        return ExitCodes.Done;
    }

    private static async Task<ContentIndexSnapshot?> ReadIndexOrWarnAsync(CliServices cli, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            return await cli.IndexSnapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException)
        {
            error.WriteLine($"warning: The content index could not be read. {exception.Message}");
            return null;
        }
    }

    private static string DescribeIndexState(bool? inIndex) => inIndex switch
    {
        true => "in the content index",
        false => "not in the content index",
        null => "content index not available",
    };

    private static string ModCount(int count) => count == 1 ? "1 mod" : $"{count} mods";

    private static bool? IsListedMod(ContentIndexSnapshot? snapshot, string folderName)
        => snapshot?.Listings.Any(listing => ModIds.Equals(listing.Id, folderName) && listing.Authored?.Type == ContentType.Mod);

    private static string Describe(InstanceSource source) => source switch
    {
        InstanceSource.FromModPack pack => $"modpack {pack.ModPackId} {pack.Version}",
        _ => "custom",
    };

    /// <summary>One entry of <c>instance list --json</c>.</summary>
    private sealed record InstanceView(Guid Id, string Name, bool Active, InstanceSourceView Source, DateTimeOffset CreatedAt, DateTimeOffset? LastPlayedAt)
    {
        public static InstanceView From(Instance instance, bool active, DateTimeOffset? lastPlayedAt)
            => new(instance.InstanceId, instance.Name, active, InstanceSourceView.From(instance.Source), instance.CreatedAt, lastPlayedAt);
    }

    /// <summary><c>instance show --json</c>.</summary>
    private sealed record InstanceDetailsView(Guid Id, string Name, bool Active, InstanceSourceView Source, DateTimeOffset CreatedAt, int ModCount, IReadOnlyList<string> LaunchArguments, DateTimeOffset? LastPlayedAt, PlaytimeView Playtime)
    {
        public static InstanceDetailsView From(Instance instance, bool active, DateTimeOffset? lastPlayedAt, InstancePlaytime playtime)
            => new(instance.InstanceId, instance.Name, active, InstanceSourceView.From(instance.Source), instance.CreatedAt, instance.Mods.Count, instance.LaunchArguments, lastPlayedAt, PlaytimeView.From(playtime));
    }

    /// <summary>The JSON shape of <c>instance arguments</c>, <c>set-arguments</c> and <c>clear-arguments</c>.</summary>
    private sealed record LaunchArgumentsView(Guid Id, string Name, IReadOnlyList<string> Arguments)
    {
        public static LaunchArgumentsView From(Instance instance) => new(instance.InstanceId, instance.Name, instance.LaunchArguments);
    }

    private sealed record PlaytimeView(long TotalSeconds, int Sessions, bool IncludesRunningSession, int UnreadableLogs, bool Known)
    {
        public static PlaytimeView From(InstancePlaytime playtime)
            => new((long)playtime.Total.TotalSeconds, playtime.Sessions, playtime.IncludesRunningSession, playtime.UnreadableLogs, playtime.IsKnown);
    }

    private sealed record InstanceSourceView(string Kind, string? ModPackId, string? Version)
    {
        public static InstanceSourceView From(InstanceSource source) => source switch
        {
            InstanceSource.FromModPack pack => new("modpack", pack.ModPackId, pack.Version.ToString()),
            _ => new("custom", null, null),
        };
    }

    private sealed record CreateView(Guid Id, string Name, bool Activated);

    /// <summary>The JSON shape of <c>instance deactivate</c>. The id and name are of the instance that was active, and null when none was.</summary>
    private sealed record DeactivateView(bool Deactivated, Guid? Id, string? Name);

    private sealed record ModView(string Id, bool Enabled);

    /// <summary>The JSON shape of <c>instance scan</c>, one list per direction.</summary>
    private sealed record ScanView(IReadOnlyList<ForeignModView> ForeignFolders, IReadOnlyList<MissingModView> MissingMods);

    /// <summary>One recorded mod of <c>instance scan --json</c> whose folder is gone.</summary>
    private sealed record MissingModView(string Id, string Version, bool? InIndex);

    /// <summary>One folder of <c>instance scan --json</c> that Borea did not install.</summary>
    private sealed record ForeignModView(string Folder, bool? InIndex, IReadOnlyList<DependencyView> Dependencies, string? DependencyReadError)
    {
        public static ForeignModView From(ForeignMod mod, ContentIndexSnapshot? snapshot)
            => new(
                mod.FolderName,
                IsListedMod(snapshot, mod.FolderName),
                mod.Dependencies.Select(dependency => new DependencyView(dependency.ModId, dependency.Optional)).ToList(),
                mod.DependencyReadError);
    }

    private sealed record DependencyView(string Id, bool Optional);

    /// <summary>The JSON shape of <c>instance import-profile --dry-run</c>.</summary>
    private sealed record ImportPreviewView(string Name, IReadOnlyList<ProfileModView> Mods);

    private sealed record ProfileModView(string Folder, bool Enabled, bool? InIndex);

    /// <summary>The JSON shape of <c>instance import-profile</c>. A copy without a version is a manual install.</summary>
    private sealed record ImportView(Guid Id, string Name, bool Activated, IReadOnlyList<ImportedModView> Mods);

    private sealed record ImportedModView(string Folder, bool Enabled, bool ManifestEntry, string? Version, string? MatchError)
    {
        public static ImportedModView From(SharedProfileImportedMod mod)
            => new(mod.FolderName, mod.Enabled, mod.HasManifestEntry, mod.Release?.Version.ToString(), mod.MatchError);
    }
}
