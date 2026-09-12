using System.CommandLine;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Cli.Commands;

internal static class ModInstallCommands
{
    public static Command BuildInstall(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = ArgumentRules.Text("mod-id", "The mod id to install.");
        var version = new Option<string?>("--version") { Description = "Install this exact version." };
        var instance = ArgumentRules.Instance();
        var recommended = new Option<bool>("--with-recommended") { Description = "Install recommended dependencies." };
        var alternatives = new Option<string[]>("--alternative") { Description = "Select a required alternative as choice-key=mod-id." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the plan from the cached index without writing files." };
        var command = new Command("install", "Install a mod and its required dependencies.");
        command.Arguments.Add(id);
        command.Options.Add(version);
        command.Options.Add(instance);
        command.Options.Add(recommended);
        command.Options.Add(alternatives);
        command.Options.Add(dryRun);
        command.SetAction((parse, cancellationToken) => CommandRunner.RunAsync(parse, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parse.GetValue(instance)).ConfigureAwait(false);
            var modId = parse.GetRequiredValue(id);
            var existing = target.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));
            if (existing is { Ownership: not ModInstallOwnership.Borea })
                throw new InvalidOperationException($"Borea does not own the files of '{existing.ModId}'.");
            var exactText = parse.GetValue(version);
            var isDryRun = parse.GetValue(dryRun);
            if (isDryRun)
                await RequireCachedIndexAsync(cli, ct).ConfigureAwait(false);
            var repository = isDryRun ? cli.ReadOnlyMods : cli.Mods;
            var release = exactText is null
                ? await repository.GetLatestReleaseAsync(modId, ct).ConfigureAwait(false)
                : await repository.GetReleaseAsync(modId, ModVersion.Parse(exactText), ct).ConfigureAwait(false);
            if (release is null)
                throw new InvalidOperationException(exactText is null ? $"No release is available for '{modId}'." : $"Release {exactText} of '{modId}' is not available.");

            var plan = await PlanAsync(cli, target, repository, [new RequestedMod(release, InstallReason.Manual, exactText is not null)], parse.GetValue(recommended), ParseAlternatives(parse.GetValue(alternatives)), ct).ConfigureAwait(false);
            PrintPlan(output, plan);
            if (parse.GetValue(dryRun))
                return plan.IsReady ? ExitCodes.Done : ExitCodes.Failed;

            await ExecuteAsync(cli, plan, ct).ConfigureAwait(false);
            return ExitCodes.Done;
        }));
        return command;
    }

    public static Command BuildRemove(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = ArgumentRules.Text("mod-id", "The installed mod id to remove.");
        var instance = ArgumentRules.Instance();
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the operation without writing files." };
        var command = new Command("remove", "Remove a mod that Borea installed.");
        command.Arguments.Add(id);
        command.Options.Add(instance);
        command.Options.Add(dryRun);
        command.SetAction((parse, cancellationToken) => CommandRunner.RunAsync(parse, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parse.GetValue(instance)).ConfigureAwait(false);
            var modId = parse.GetRequiredValue(id);
            var installed = target.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId))
                ?? throw new InvalidOperationException($"Mod '{modId}' is not installed in '{target.Name}'.");
            if (installed.Ownership != ModInstallOwnership.Borea)
                throw new InvalidOperationException($"Borea does not own the files of '{installed.ModId}'.");

            var active = await cli.ModState.IsActiveAsync(target.InstanceId, installed.ModId, ct).ConfigureAwait(false);
            var check = new ModDependencyResolver().CheckUninstall(target, installed.ModId, installed.Version, active);
            if (check.DependentModIds.Count > 0)
                throw new InvalidOperationException($"'{installed.ModId}' is required by {string.Join(", ", check.DependentModIds)}.");

            output.WriteLine($"Remove {installed.ModId} {installed.Version} from '{target.Name}'.");
            if (!parse.GetValue(dryRun))
                await cli.Uninstaller.UninstallAsync(target.InstanceId, installed.ModId, ct).ConfigureAwait(false);
            return ExitCodes.Done;
        }));
        return command;
    }

    public static Command BuildUpdate(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = new Argument<string?>("mod-id") { Description = "The mod id to update. Omit it to update all managed mods.", Arity = ArgumentArity.ZeroOrOne };
        var instance = ArgumentRules.Instance();
        var recommended = new Option<bool>("--with-recommended") { Description = "Install recommended dependencies." };
        var alternatives = new Option<string[]>("--alternative") { Description = "Select a required alternative as choice-key=mod-id." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the plan from the cached index without writing files." };
        var command = new Command("update", "Update one mod or all managed mods.");
        command.Arguments.Add(id);
        command.Options.Add(instance);
        command.Options.Add(recommended);
        command.Options.Add(alternatives);
        command.Options.Add(dryRun);
        command.SetAction((parse, cancellationToken) => CommandRunner.RunAsync(parse, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parse.GetValue(instance)).ConfigureAwait(false);
            var selectedId = parse.GetValue(id);
            var selected = selectedId is null
                ? target.Mods.Where(mod => mod.Ownership == ModInstallOwnership.Borea).ToList()
                : target.Mods.Where(mod => ModIds.Equals(mod.ModId, selectedId)).ToList();
            if (selected.Count == 0 && selectedId is not null)
                throw new InvalidOperationException($"Mod '{selectedId}' is not installed in '{target.Name}'.");
            if (selected.Any(mod => mod.Ownership != ModInstallOwnership.Borea))
                throw new InvalidOperationException($"Borea does not own the files of '{selected.Single(mod => mod.Ownership != ModInstallOwnership.Borea).ModId}'.");

            var isDryRun = parse.GetValue(dryRun);
            if (isDryRun)
                await RequireCachedIndexAsync(cli, ct).ConfigureAwait(false);
            var repository = isDryRun ? cli.ReadOnlyMods : cli.Mods;
            var requested = selected.Select(mod => new RequestedMod(mod.Metadata, mod.Reason, Exact: false)).ToList();
            var plan = await PlanAsync(cli, target, repository, requested, parse.GetValue(recommended), ParseAlternatives(parse.GetValue(alternatives)), ct).ConfigureAwait(false);
            PrintPlan(output, plan);
            if (parse.GetValue(dryRun))
                return plan.IsReady ? ExitCodes.Done : ExitCodes.Failed;

            await ExecuteAsync(cli, plan, ct).ConfigureAwait(false);
            return ExitCodes.Done;
        }));
        return command;
    }

    private static async Task<InstallPlan> PlanAsync(CliServices cli, Instance instance, IModRepository repository, IReadOnlyList<RequestedMod> requested, bool withRecommended, IReadOnlyDictionary<string, string> alternatives, CancellationToken cancellationToken)
    {
        var gameVersion = cli.InstalledVersion.GetInstalledVersion()?.Version;
        var request = new InstallPlanningRequest(instance, requested, repository, gameVersion, CurrentPlatform(), Alternatives: alternatives);
        var plan = await cli.InstallPlanner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (!withRecommended)
            return plan;

        var selected = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var added = false;
            foreach (var choice in plan.Choices.Where(choice => choice.Kind == "recommendation"))
                added |= selected.Add(choice.Key);
            if (!added)
                return plan;

            plan = await cli.InstallPlanner.PlanAsync(request with { Recommended = selected }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(CliServices cli, InstallPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.IsReady)
            throw new InvalidOperationException("The install plan has unresolved choices or conflicts.");

        var fresh = await cli.Instances.GetByIdAsync(plan.InstanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Instance '{plan.InstanceId}' no longer exists.");
        if (!plan.InstanceState.Matches(fresh))
            throw new InvalidOperationException("The instance changed after Borea planned the operation. Run the command again.");

        var expectedState = plan.InstanceState;
        foreach (var operation in plan.Operations)
        {
            fresh = await cli.Instances.GetByIdAsync(plan.InstanceId).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Instance '{plan.InstanceId}' no longer exists.");
            if (!expectedState.Matches(fresh))
                throw new InvalidOperationException("The instance changed while Borea executed the operation. Run the command again.");

            var current = fresh.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, operation.Release.ModId));
            if (current is null)
            {
                var result = await cli.Installer.InstallGuardedAsync(plan.InstanceId, operation.Release, operation.Reason, enable: true, expectedState, cancellationToken: cancellationToken).ConfigureAwait(false);
                expectedState = result.State;
            }
            else
            {
                var result = await cli.Replacer.ReplaceGuardedAsync(plan.InstanceId, current, operation.Release, expectedState, cancellationToken: cancellationToken).ConfigureAwait(false);
                expectedState = result.State;
            }
        }
    }

    private static void PrintPlan(TextWriter output, InstallPlan plan)
    {
        foreach (var warning in plan.Warnings)
            output.WriteLine($"warning: {warning.Message}");
        foreach (var operation in plan.Operations)
            output.WriteLine($"{(operation.Reason == InstallReason.Manual ? "Install" : "Install dependency")} {operation.Release.ModId} {operation.Release.Version}.");
        foreach (var choice in plan.UnresolvedChoices)
            output.WriteLine($"choice: {choice.Message}");
        foreach (var choice in plan.Choices.Where(choice => choice.Selected is null))
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

    private static OsPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? OsPlatform.Windows : OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs;

    private static async Task RequireCachedIndexAsync(CliServices cli, CancellationToken cancellationToken)
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

    private static IReadOnlyDictionary<string, string> ParseAlternatives(string[]? values)
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
