using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Cli.Commands;

internal static class ModInstallCommands
{
    public static Command BuildInstall(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = ArgumentRules.Text("mod-id", "The id of the mod, vehicle or save to install.");
        var version = new Option<string?>("--version") { Description = "Install this exact version." };
        var instance = ArgumentRules.Instance();
        var recommended = new Option<bool>("--with-recommended") { Description = "Install recommended dependencies." };
        var alternatives = new Option<string[]>("--alternative") { Description = "Select a required alternative as choice-key=mod-id." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the plan from the cached index without writing files." };
        var channel = ArgumentRules.Channel();
        var name = NameOption();
        var allowMissing = AllowMissingOption();
        var json = ArgumentRules.Json();
        var command = new Command("install", "Install a mod and its required dependencies, or a vehicle or save and the mods it needs.");
        command.Arguments.Add(id);
        command.Options.Add(version);
        command.Options.Add(instance);
        command.Options.Add(recommended);
        command.Options.Add(alternatives);
        command.Options.Add(dryRun);
        command.Options.Add(channel);
        command.Options.Add(name);
        command.Options.Add(allowMissing);
        command.Options.Add(json);
        command.SetAction((parse, cancellationToken) => CommandRunner.RunAsync(parse, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parse.GetValue(instance)).ConfigureAwait(false);
            var modId = parse.GetRequiredValue(id);
            var isDryRun = parse.GetValue(dryRun);
            if (isDryRun)
                await RequireCachedIndexAsync(cli, ct).ConfigureAwait(false);
            var releaseChannel = ArgumentRules.ChannelOrSaved(parse.GetValue(channel), cli.Settings.ReleaseChannel);
            if (await (isDryRun ? cli.ReadOnlyUserContent : cli.UserContent).FindAsync(modId, ct).ConfigureAwait(false) is { } content)
            {
                var options = new UserContentCommands.Options(parse.GetValue(name), parse.GetValue(version), releaseChannel, parse.GetValue(recommended), ParseAlternatives(parse.GetValue(alternatives)), parse.GetValue(allowMissing), DiscardChanges: false, isDryRun, parse.GetValue(json));
                return await UserContentCommands.InstallAsync(cli, target, content, options, output, error, ct).ConfigureAwait(false);
            }

            RequireModOptions(parse, modId, name, allowMissing, json);
            var existing = target.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));
            if (existing is { Ownership: not ModInstallOwnership.Borea })
                throw new InvalidOperationException($"Borea does not own the files of '{existing.ModId}'. Run 'borea instance take-ownership' to let Borea install the recorded release over its folder.");
            var exactText = parse.GetValue(version);
            var repository = isDryRun ? cli.ReadOnlyMods : cli.Mods;
            var release = exactText is null
                ? await repository.GetLatestReleaseInChannelAsync(modId, releaseChannel, ct).ConfigureAwait(false)
                : await repository.GetReleaseAsync(modId, ModVersion.Parse(exactText), ct).ConfigureAwait(false);
            if (release is null)
                throw new InvalidOperationException(exactText is null ? $"No release of '{modId}' in the {releaseChannel.ToName()} channel is available." : $"Release {exactText} of '{modId}' is not available.");

            var plan = await PlanAsync(cli, target, repository, [new RequestedMod(release, InstallReason.Manual, exactText is not null)], releaseChannel, parse.GetValue(recommended), ParseAlternatives(parse.GetValue(alternatives)), ct).ConfigureAwait(false);
            PrintPlan(output, plan);
            if (parse.GetValue(dryRun))
            {
                if (!plan.IsReady)
                    return ExitCodes.Failed;

                await PrintInstallStepsAsync(output, cli, plan, ct).ConfigureAwait(false);
                return ExitCodes.Done;
            }

            await ExecuteAsync(cli, plan, error, ct).ConfigureAwait(false);
            await PrintInstallStepsAsync(output, cli, plan, ct).ConfigureAwait(false);
            return ExitCodes.Done;
        }));
        return command;
    }

    public static Command BuildRemove(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = ArgumentRules.Text("mod-id", "The id of the installed mod, vehicle or save to remove.");
        var instance = ArgumentRules.Instance();
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the operation without writing files." };
        var discardChanges = DiscardChangesOption();
        var json = ArgumentRules.Json();
        var command = new Command("remove", "Remove a mod, vehicle or save that Borea installed.");
        command.Arguments.Add(id);
        command.Options.Add(instance);
        command.Options.Add(dryRun);
        command.Options.Add(discardChanges);
        command.Options.Add(json);
        command.SetAction((parse, cancellationToken) => CommandRunner.RunAsync(parse, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parse.GetValue(instance)).ConfigureAwait(false);
            var modId = parse.GetRequiredValue(id);
            if (await UserContentCommands.FindRecordAsync(cli, target, modId, ct).ConfigureAwait(false) is { } record)
            {
                var options = new UserContentCommands.Options(null, null, cli.Settings.ReleaseChannel, false, new Dictionary<string, string>(), false, parse.GetValue(discardChanges), parse.GetValue(dryRun), parse.GetValue(json));
                return await UserContentCommands.RemoveAsync(cli, target, record, options, output, ct).ConfigureAwait(false);
            }

            RequireModOptions(parse, modId, discardChanges, json);
            var installed = target.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId))
                ?? throw new InvalidOperationException($"Mod '{modId}' is not installed in '{target.Name}'.");
            if (installed.Ownership != ModInstallOwnership.Borea)
                throw new InvalidOperationException($"Borea does not own the files of '{installed.ModId}'. Run 'borea instance take-ownership' to let Borea install the recorded release over its folder.");

            var active = await cli.ModState.IsActiveAsync(target.InstanceId, installed.ModId, ct).ConfigureAwait(false);
            var check = new ModDependencyResolver().CheckUninstall(target, installed.ModId, installed.Version, active);
            if (check.DependentModIds.Count > 0)
                throw new InvalidOperationException($"'{installed.ModId}' is required by {string.Join(", ", check.DependentModIds)}.");

            output.WriteLine($"Remove {installed.ModId} {installed.Version} from '{target.Name}'.");
            if (!parse.GetValue(dryRun))
                await cli.Uninstaller.UninstallAsync(target.InstanceId, installed.ModId, ct).ConfigureAwait(false);
            var listing = await FindCachedListingAsync(cli, installed.ModId, ct).ConfigureAwait(false);
            ContentOutput.WriteSteps(output, $"Uninstall steps for {installed.ModId}:", listing?.Install?.Uninstall);
            return ExitCodes.Done;
        }));
        return command;
    }

    public static Command BuildUpdate(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = new Argument<string?>("mod-id") { Description = "The id of the mod, vehicle or save to update. Omit it to update all managed mods; vehicles and saves update one at a time.", Arity = ArgumentArity.ZeroOrOne };
        var instance = ArgumentRules.Instance();
        var recommended = new Option<bool>("--with-recommended") { Description = "Install recommended dependencies." };
        var alternatives = new Option<string[]>("--alternative") { Description = "Select a required alternative as choice-key=mod-id." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the plan from the cached index without writing files." };
        var channel = ArgumentRules.Channel();
        var name = NameOption();
        var allowMissing = AllowMissingOption();
        var discardChanges = DiscardChangesOption();
        var json = ArgumentRules.Json();
        var command = new Command("update", "Update one mod or all managed mods, or one vehicle or save.");
        command.Arguments.Add(id);
        command.Options.Add(instance);
        command.Options.Add(recommended);
        command.Options.Add(alternatives);
        command.Options.Add(dryRun);
        command.Options.Add(channel);
        command.Options.Add(name);
        command.Options.Add(allowMissing);
        command.Options.Add(discardChanges);
        command.Options.Add(json);
        command.SetAction((parse, cancellationToken) => CommandRunner.RunAsync(parse, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parse.GetValue(instance)).ConfigureAwait(false);
            var selectedId = parse.GetValue(id);
            if (selectedId is not null && await UserContentCommands.FindRecordAsync(cli, target, selectedId, ct).ConfigureAwait(false) is { } record)
            {
                var dry = parse.GetValue(dryRun);
                if (dry)
                    await RequireCachedIndexAsync(cli, ct).ConfigureAwait(false);
                var options = new UserContentCommands.Options(parse.GetValue(name), null, ArgumentRules.ChannelOrSaved(parse.GetValue(channel), cli.Settings.ReleaseChannel), parse.GetValue(recommended), ParseAlternatives(parse.GetValue(alternatives)), parse.GetValue(allowMissing), parse.GetValue(discardChanges), dry, parse.GetValue(json));
                return await UserContentCommands.UpdateAsync(cli, target, record, options, output, error, ct).ConfigureAwait(false);
            }

            RequireModOptions(parse, selectedId, name, allowMissing, discardChanges, json);
            var selected = selectedId is null
                ? target.Mods.Where(mod => mod.Ownership == ModInstallOwnership.Borea).ToList()
                : target.Mods.Where(mod => ModIds.Equals(mod.ModId, selectedId)).ToList();
            if (selected.Count == 0 && selectedId is not null)
                throw new InvalidOperationException($"Mod '{selectedId}' is not installed in '{target.Name}'.");
            if (selected.Any(mod => mod.Ownership != ModInstallOwnership.Borea))
                throw new InvalidOperationException($"Borea does not own the files of '{selected.Single(mod => mod.Ownership != ModInstallOwnership.Borea).ModId}'. Run 'borea instance take-ownership' to let Borea install the recorded release over its folder.");

            var isDryRun = parse.GetValue(dryRun);
            if (isDryRun)
                await RequireCachedIndexAsync(cli, ct).ConfigureAwait(false);
            var repository = isDryRun ? cli.ReadOnlyMods : cli.Mods;
            var requested = selected.Select(mod => new RequestedMod(mod.Metadata, mod.Reason, Exact: false)).ToList();
            var releaseChannel = ArgumentRules.ChannelOrSaved(parse.GetValue(channel), cli.Settings.ReleaseChannel);
            var plan = await PlanAsync(cli, target, repository, requested, releaseChannel, parse.GetValue(recommended), ParseAlternatives(parse.GetValue(alternatives)), ct).ConfigureAwait(false);
            foreach (var pinned in selected.Where(mod => mod.IsPinned))
                output.WriteLine($"Keep {pinned.ModId} {pinned.Version}, because it is pinned. Run 'borea unpin {pinned.ModId}' to update it.");
            PrintPlan(output, plan);
            if (parse.GetValue(dryRun))
                return plan.IsReady ? ExitCodes.Done : ExitCodes.Failed;

            await ExecuteAsync(cli, plan, error, ct).ConfigureAwait(false);
            return ExitCodes.Done;
        }));
        return command;
    }

    private static async Task<InstallPlan> PlanAsync(CliServices cli, Instance instance, IModRepository repository, IReadOnlyList<RequestedMod> requested, ReleaseChannel channel, bool withRecommended, IReadOnlyDictionary<string, string> alternatives, CancellationToken cancellationToken)
    {
        var gameVersion = cli.InstalledVersion.GetInstalledVersion()?.Version;
        var request = new InstallPlanningRequest(instance, requested, repository, gameVersion, cli.GamePlatform.Current, Alternatives: alternatives, Channel: channel);
        var plan = await cli.InstallPlanner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (!withRecommended)
            return plan;

        var selected = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var added = false;
            foreach (var choice in plan.Choices.Where(choice => choice.Kind == PlanningChoiceKind.Recommendation))
                added |= selected.Add(choice.Key);
            if (!added)
                return plan;

            plan = await cli.InstallPlanner.PlanAsync(request with { Recommended = selected }, cancellationToken).ConfigureAwait(false);
        }
    }

    // built from the CLI's own services, so a test that replaces the installer or the replacer replaces them here too
    internal static async Task ExecuteAsync(CliServices cli, InstallPlan plan, TextWriter error, CancellationToken cancellationToken)
    {
        var stop = new InstallStop();
        using var registration = cancellationToken.Register(stop.Request);
        try
        {
            await new InstallPlanExecutor(cli.Instances, cli.Installer, cli.Replacer, cli.SpaceCheck).ExecuteAsync(plan, enable: true, new InstallProgressOutput(error), stop).ConfigureAwait(false);
        }
        catch (InstallStoppedException stopped)
        {
            error.WriteLine($"Stopped. {stopped.Completed} of {stopped.Total} mods installed.");
            throw;
        }
    }

    internal static void PrintPlan(TextWriter output, InstallPlan plan)
    {
        foreach (var warning in plan.Warnings)
            output.WriteLine($"warning: {warning.Message}");
        foreach (var operation in plan.Operations)
            output.WriteLine($"{(operation.Reason == InstallReason.Manual ? "Install" : "Install dependency")} {operation.Release.ModId} {operation.Release.Version}.");
        foreach (var choice in plan.UnresolvedChoices)
            output.WriteLine($"choice: {choice.Message}");
        foreach (var choice in plan.Choices.Where(choice => choice.Kind == PlanningChoiceKind.Alternative && choice.Selected is null))
            output.WriteLine($"choice option: {choice.Key} = {string.Join(", ", choice.Options)}");
        foreach (var conflict in plan.Conflicts)
        {
            var selection = plan.Selections.FirstOrDefault(item => ModIds.Equals(item.Release.ModId, conflict.ModId));
            output.WriteLine(conflict.Code == "incompatible" && selection is not null
                ? $"conflict: {conflict.Message} Requires game {selection.Release.GameMin}."
                : $"conflict: {conflict.Message}");
        }
        if (plan.Operations.Count == 0 && plan.IsReady)
            output.WriteLine("Nothing to do.");
    }

    /// <summary>The install steps of every listing the plan installs.</summary>
    private static async Task PrintInstallStepsAsync(TextWriter output, CliServices cli, InstallPlan plan, CancellationToken cancellationToken)
    {
        foreach (var operation in plan.Operations)
        {
            var listing = await FindCachedListingAsync(cli, operation.Release.ModId, cancellationToken).ConfigureAwait(false);
            ContentOutput.WriteSteps(output, $"Install steps for {operation.Release.ModId}:", listing?.Install?.Steps);
        }
    }

    /// <summary>
    /// The listing in the cached content index, or null when there is none. Only an index
    /// listing states steps, and an install or removal that is done must not fail on the way it reads them.
    /// </summary>
    internal static async Task<ModMetadata?> FindCachedListingAsync(CliServices cli, string id, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await cli.IndexReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return snapshot.Listings.Concat(snapshot.Vehicles).Concat(snapshot.Saves).FirstOrDefault(listing => ModIds.Equals(listing.Id, id))?.Authored;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static async Task RequireCachedIndexAsync(CliServices cli, CancellationToken cancellationToken)
    {
        try
        {
            await cli.IndexReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException("Dry-run needs a usable cached content index. Run 'borea index refresh' first.", exception);
        }
    }

    private static Option<string?> NameOption()
    {
        var option = new Option<string?>("--name") { Description = "Install a vehicle or save under this new name, when its own name is taken." };
        option.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string?>() is { } value && !GameSaveName.IsValid(value))
                result.AddError($"'{result.GetValueOrDefault<string?>()}' is a name the game would change. Use letters, digits, spaces, '-' and '_'.");
        });
        return option;
    }

    private static Option<bool> AllowMissingOption() => new("--allow-missing") { Description = "Install a vehicle or save although a mod it needs cannot be installed or enabled." };

    private static Option<bool> DiscardChangesOption() => new("--discard-changes") { Description = "Replace or remove a vehicle or save whose files changed since the install. The old folder stays in the backups." };

    /// <summary>The options of a vehicle or save make no sense for a mod, so a mod command refuses them instead of ignoring them.</summary>
    private static void RequireModOptions(ParseResult parse, string? id, params Option[] options)
    {
        var given = options.Where(option => parse.GetResult(option) is { Implicit: false }).Select(option => option.Name).ToList();
        if (given.Count > 0)
            throw new InvalidOperationException($"{string.Join(", ", given)} apply only to a vehicle or a save{(id is null ? "." : $", and Borea knows no vehicle or save '{id}' here.")}");
    }

    internal static IReadOnlyDictionary<string, string> ParseAlternatives(string[]? values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            var separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
                throw new FormatException($"Alternative '{value}' must use choice-key=mod-id.");
            if (!result.TryAdd(value[..separator], value[(separator + 1)..]))
                throw new FormatException($"Alternative choice '{value[..separator]}' was specified more than once.");
        }
        return result;
    }
}
