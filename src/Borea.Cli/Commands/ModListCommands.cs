using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea instance duplicate</c>, <c>export</c> and <c>import</c>, which create an instance from a modlist through <see cref="ModListInstaller"/>.
/// </summary>
internal static class ModListCommands
{
    public static Command BuildDuplicate(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceCommand.InstanceArgumentDescription);
        var name = NameOption("The name of the copy. The instance's name with \"(copy)\" when absent.");
        var skipUnknown = SkipUnknownOption();
        var proceedWithYanked = ProceedWithYankedOption();
        var dryRun = DryRunOption();
        var json = ArgumentRules.Json();
        var command = new Command("duplicate", "Create an instance with the same mods, versions, and enabled flags.");
        command.Arguments.Add(instance);
        command.Options.Add(name);
        command.Options.Add(skipUnknown);
        command.Options.Add(proceedWithYanked);
        command.Options.Add(dryRun);
        command.Options.Add(json);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var source = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var manifest = await cli.ModState.GetEntriesAsync(source.InstanceId, ct).ConfigureAwait(false);
            var installer = Installer(cli);
            var newName = await NameAsync(cli, installer, parseResult.GetValue(name), $"{source.Name} (copy)").ConfigureAwait(false);
            var reasons = source.Mods.ToDictionary(mod => mod.ModId, mod => mod.Reason, ModIds.Comparer);
            var context = RunContext.From(parseResult, skipUnknown, proceedWithYanked, dryRun, json, output, error);

            var request = new ModListRequest(ModList.FromInstance(source, manifest), source.Source, Repository(cli, context), GameVersion(cli), CurrentPlatform(), reasons);

            return await PlanAndInstallAsync(cli, installer, request, newName, source.ForeignMods.Select(mod => mod.FolderName).ToList(), context, ct).ConfigureAwait(false);
        }));

        return command;
    }

    public static Command BuildExport(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceCommand.InstanceArgumentDescription);
        var file = new Argument<string?>("file") { Description = "The file to write. Without it, the modlist goes to the output.", Arity = ArgumentArity.ZeroOrOne };
        file.Validators.Add(result =>
        {
            if (result.Tokens.Count > 0 && string.IsNullOrWhiteSpace(result.GetValueOrDefault<string?>()))
                result.AddError("The file cannot be empty.");
        });
        var force = new Option<bool>("--force") { Description = "Replace the file when it exists." };
        var json = ArgumentRules.Json();
        var command = new Command("export", "Write the mods of an instance, with their versions and enabled flags, as a modlist that 'instance import' reads.");
        command.Arguments.Add(instance);
        command.Arguments.Add(file);
        command.Options.Add(force);
        command.Options.Add(json);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var source = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var manifest = await cli.ModState.GetEntriesAsync(source.InstanceId, ct).ConfigureAwait(false);
            var modList = ModList.FromInstance(source, manifest);
            var text = cli.ModListFormat.Write(modList);
            var path = parseResult.GetValue(file) is { } given ? Path.GetFullPath(given) : null;

            if (path is not null)
            {
                if (File.Exists(path) && !parseResult.GetValue(force))
                    throw new InvalidOperationException($"{path} exists already. Pass --force to replace it.");

                await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);
            }

            var notExported = source.ForeignMods.Select(mod => mod.FolderName).ToList();
            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, new ExportView(source.InstanceId, source.Name, path, modList.Mods.Select(EntryView.From).ToList(), notExported));
                return ExitCodes.Done;
            }

            if (path is null)
                output.Write(text);
            else
                output.WriteLine($"Exported {modList.Mods.Count} mods of '{source.Name}' to {path}.");

            foreach (var folder in notExported)
                error.WriteLine($"warning: '{folder}' is not in the modlist, because Borea did not install it.");

            return ExitCodes.Done;
        }));

        return command;
    }

    public static Command BuildImport(Func<CancellationToken, Task<CliServices>> services)
    {
        var file = ArgumentRules.Text("file", "The modlist file that 'instance export' wrote.");
        var name = NameOption("The name of the new instance. The name in the modlist when absent.");
        var skipUnknown = SkipUnknownOption();
        var proceedWithYanked = ProceedWithYankedOption();
        var dryRun = DryRunOption();
        var json = ArgumentRules.Json();
        var command = new Command("import", "Create an instance from a modlist.");
        command.Arguments.Add(file);
        command.Options.Add(name);
        command.Options.Add(skipUnknown);
        command.Options.Add(proceedWithYanked);
        command.Options.Add(dryRun);
        command.Options.Add(json);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var path = Path.GetFullPath(parseResult.GetRequiredValue(file));
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            ModList modList;
            try
            {
                modList = cli.ModListFormat.Read(text);
            }
            catch (FormatException exception)
            {
                throw new FormatException($"{path} is not a modlist that Borea can read. {exception.Message}", exception);
            }

            var wanted = modList.Name ?? Path.GetFileNameWithoutExtension(path);
            if (parseResult.GetValue(name) is null && string.IsNullOrWhiteSpace(wanted))
                throw new InvalidOperationException("The modlist has no name. Pass --name.");

            var installer = Installer(cli);
            var newName = await NameAsync(cli, installer, parseResult.GetValue(name), wanted).ConfigureAwait(false);
            var context = RunContext.From(parseResult, skipUnknown, proceedWithYanked, dryRun, json, output, error);

            var request = new ModListRequest(modList, InstanceSource.Custom.Value, Repository(cli, context), GameVersion(cli), CurrentPlatform());

            return await PlanAndInstallAsync(cli, installer, request, newName, [], context, ct).ConfigureAwait(false);
        }));

        return command;
    }

    private static async Task<int> PlanAndInstallAsync(
        CliServices cli,
        ModListInstaller installer,
        ModListRequest request,
        string name,
        IReadOnlyList<string> notCopied,
        RunContext context,
        CancellationToken cancellationToken)
    {
        if (context.DryRun)
            await ModInstallCommands.RequireCachedIndexAsync(cli, cancellationToken).ConfigureAwait(false);

        var plan = await installer.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        List<ModListItem> unknown = context.SkipUnknown ? [] : plan.Unknown.ToList();
        var yanked = plan.Yanked.Where(item => !context.ProceedWithYanked.Contains(item.Entry.ModId)).ToList();
        if (!context.Json)
            WriteHuman(context.Output, plan, name, notCopied);

        InstanceCreateResult? created = null;
        if (plan.Plan.IsReady && unknown.Count == 0 && yanked.Count == 0 && !context.DryRun)
            created = await installer.InstallAsync(plan, name, new InstallProgressOutput(context.Error), cancellationToken: cancellationToken).ConfigureAwait(false);

        if (context.Json)
            JsonOutput.Write(context.Output, PlanView.From(plan, name, created, notCopied));
        else if (created is not null)
            context.Output.WriteLine(InstanceCommand.DescribeCreated(created));

        if (unknown.Count > 0)
            context.Error.WriteLine($"error: No source lists {Describe(unknown)}. Pass --skip-unknown to create the instance without them.");
        if (yanked.Count > 0)
            context.Error.WriteLine($"error: These releases are yanked: {Describe(yanked)}. Pass --proceed-with-yanked with each mod id to install them anyway.");
        if (unknown.Count > 0 || yanked.Count > 0)
            return ExitCodes.Failed;

        if (!plan.Plan.IsReady)
        {
            context.Error.WriteLine("error: The mods cannot be installed together. The conflicts and choices above say why.");
            return ExitCodes.Failed;
        }

        return ExitCodes.Done;
    }

    private static string Describe(IEnumerable<ModListItem> items) => string.Join(", ", items.Select(item => $"{item.Entry.ModId} {item.Entry.Version}"));

    private static ModListInstaller Installer(CliServices cli) =>
        new(cli.Instances, cli.InstallPlanner, new InstallPlanExecutor(cli.Instances, cli.Installer, cli.Replacer), cli.ModState);

    private static async Task<string> NameAsync(CliServices cli, ModListInstaller installer, string? given, string wanted)
    {
        if (given is null)
            return await installer.FreeNameAsync(wanted).ConfigureAwait(false);

        var trimmed = given.Trim();
        if (!await cli.Instances.IsNameAvailableAsync(trimmed).ConfigureAwait(false))
            throw new InvalidOperationException($"Instance name '{trimmed}' is already in use.");
        return trimmed;
    }

    private static IModRepository Repository(CliServices cli, RunContext context) => context.DryRun ? cli.ReadOnlyMods : cli.Mods;

    private static GameVersion? GameVersion(CliServices cli) => cli.InstalledVersion.GetInstalledVersion()?.Version;

    private static Option<string?> NameOption(string description)
    {
        var option = new Option<string?>("--name") { Description = description };
        option.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string?>()))
                result.AddError("The --name value cannot be empty.");
        });
        return option;
    }

    private static Option<bool> SkipUnknownOption() =>
        new("--skip-unknown") { Description = "Create the instance without the listed releases that no source knows." };

    private static Option<string[]> ProceedWithYankedOption()
    {
        var option = new Option<string[]>("--proceed-with-yanked")
        {
            Description = "Install the yanked release the modlist names for this mod id. Repeat it for each yanked mod.",
        };
        option.Validators.Add(result =>
        {
            foreach (var value in result.GetValueOrDefault<string[]>() ?? [])
            {
                if (!ModIds.IsValid(value))
                    result.AddError($"'{value}' is not a valid content id.");
            }
        });
        return option;
    }

    private static Option<bool> DryRunOption() =>
        new("--dry-run") { Description = "Print the plan from the cached index without creating the instance." };

    private static OsPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? OsPlatform.Windows : OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs;

    private static void WriteHuman(TextWriter output, ModListPlan plan, string name, IReadOnlyList<string> notCopied)
    {
        output.WriteLine($"Instance '{name}' from {plan.Items.Count} listed mods:");
        foreach (var item in plan.Items)
        {
            var subject = $"{item.Entry.ModId} {item.Entry.Version}";
            output.WriteLine(item switch
            {
                { IsUnknown: true } => $"  unknown   {subject}: No source lists this release.",
                { IsYanked: true } => $"  yanked    {subject}: {item.Release!.YankedReason ?? "The release is yanked."}",
                _ => $"  {(item.Entry.Enabled ? "enabled " : "disabled")}  {subject}",
            });
        }

        foreach (var folder in notCopied)
            output.WriteLine($"not copied: '{folder}', because Borea did not install it.");

        // the yanked lines above already name the releases the planner warns about
        var planned = plan.Plan;
        ModInstallCommands.PrintPlan(output, new InstallPlan(
            planned.InstanceId,
            planned.InstanceState,
            planned.Selections,
            planned.Operations,
            planned.Warnings.Where(warning => warning.Code != "yanked").ToList(),
            planned.UnresolvedChoices,
            planned.Conflicts,
            planned.Choices));
    }

    private static string ReasonName(InstallReason reason) => reason switch
    {
        InstallReason.Manual => "manual",
        InstallReason.ModPack => "modpack",
        InstallReason.Dependency => "dependency",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private sealed record RunContext(bool SkipUnknown, IReadOnlySet<string> ProceedWithYanked, bool DryRun, bool Json, TextWriter Output, TextWriter Error)
    {
        public static RunContext From(ParseResult parseResult, Option<bool> skipUnknown, Option<string[]> proceedWithYanked, Option<bool> dryRun, Option<bool> json, TextWriter output, TextWriter error) => new(
            parseResult.GetValue(skipUnknown),
            new HashSet<string>(parseResult.GetValue(proceedWithYanked) ?? [], ModIds.Comparer),
            parseResult.GetValue(dryRun),
            parseResult.GetValue(json),
            output,
            error);
    }

    private sealed record EntryView(string Id, string Version, bool Enabled)
    {
        public static EntryView From(ModListEntry entry) => new(entry.ModId, entry.Version.ToString(), entry.Enabled);
    }

    private sealed record ExportView(Guid InstanceId, string Name, string? File, IReadOnlyList<EntryView> Mods, IReadOnlyList<string> NotExported);

    private sealed record PlanView(
        Guid? InstanceId,
        string Name,
        bool Created,
        bool Activated,
        IReadOnlyList<ItemView> Mods,
        IReadOnlyList<string> NotCopied,
        IReadOnlyList<OperationView> Operations,
        IReadOnlyList<MessageView> Warnings,
        IReadOnlyList<MessageView> UnresolvedChoices,
        IReadOnlyList<MessageView> Conflicts)
    {
        public static PlanView From(ModListPlan plan, string name, InstanceCreateResult? created, IReadOnlyList<string> notCopied) => new(
            created?.Instance.InstanceId,
            name,
            created is not null,
            created?.Activated ?? false,
            plan.Items.Select(ItemView.From).ToList(),
            notCopied,
            plan.Plan.Operations.Select(operation => new OperationView(operation.Release.ModId, operation.Release.Version.ToString(), ReasonName(operation.Reason))).ToList(),
            plan.Plan.Warnings.Select(MessageView.From).ToList(),
            plan.Plan.UnresolvedChoices.Select(MessageView.From).ToList(),
            plan.Plan.Conflicts.Select(MessageView.From).ToList());
    }

    private sealed record ItemView(string Id, string Version, bool Enabled, string State, string? YankedReason)
    {
        public static ItemView From(ModListItem item) => new(
            item.Entry.ModId,
            item.Entry.Version.ToString(),
            item.Entry.Enabled,
            item.IsUnknown ? "unknown" : item.IsYanked ? "yanked" : "available",
            item.IsYanked ? item.Release!.YankedReason : null);
    }

    private sealed record OperationView(string Id, string Version, string Reason);

    private sealed record MessageView(string Id, string Code, string Message)
    {
        public static MessageView From(PlanningMessage message) => new(message.ModId, message.Code, message.Message);
    }
}
