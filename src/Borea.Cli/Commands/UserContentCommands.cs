using Borea.Cli.Output;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.UserContent;

namespace Borea.Cli.Commands;

/// <summary>
/// The install, update and removal of a vehicle or save by <c>borea install</c>, <c>update</c> and <c>remove</c> (RFC 0085).
/// The ids are one namespace, so the listing's type decides which path a command takes.
/// </summary>
internal static class UserContentCommands
{
    /// <param name="Name">The new name from --name, or null.</param>
    /// <param name="Version">The exact version from --version, or null for the newest in the channel.</param>
    public sealed record Options(string? Name, string? Version, ReleaseChannel Channel, bool WithRecommended, IReadOnlyDictionary<string, string> Alternatives, bool AllowMissing, bool DiscardChanges, bool DryRun, bool Json);

    /// <summary>The record of the listing in the instance, or null when Borea installed no vehicle or save of that id.</summary>
    public static async Task<InstalledContentRecord?> FindRecordAsync(CliServices cli, Instance instance, string id, CancellationToken cancellationToken) =>
        (await cli.InstalledContent.ListAsync(instance.InstanceId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(record => ModIds.Equals(record.ListingId, id));

    /// <summary>
    /// The recorded vehicles and saves of the instance and the mods its manifest disables. A record or manifest file
    /// Borea cannot read gives neither, so it never stops a mod change that only warns about them.
    /// </summary>
    public static async Task<(IReadOnlyList<InstalledContentRecord> Records, IReadOnlyList<string> Inactive)> RecordedStateAsync(CliServices cli, Instance instance, CancellationToken cancellationToken)
    {
        try
        {
            var records = await cli.InstalledContent.ListAsync(instance.InstanceId, cancellationToken).ConfigureAwait(false);
            if (records.Count == 0)
                return ([], []);

            var entries = await cli.ModState.GetEntriesAsync(instance.InstanceId, cancellationToken).ConfigureAwait(false);
            return (records, entries.Where(entry => !entry.Enabled).Select(entry => entry.ModId).ToList());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return ([], []);
        }
    }

    /// <summary>Names each recorded vehicle or save that the game no longer loads in full without <paramref name="modId"/>. It only warns, because only an incompatible release blocks.</summary>
    public static void WriteNeeding(TextWriter error, string modId, IEnumerable<InstalledContentRecord> content)
    {
        foreach (var record in content)
        {
            error.WriteLine(record.Type == ContentType.Save
                ? $"warning: The save {record.FolderName} needs {modId}, so the game leaves out every vehicle of that save that needs it, and saving again loses those vehicles."
                : $"warning: The vehicle {record.FolderName} needs {modId}, so the game refuses that vehicle.");
        }
    }

    public static async Task<int> InstallAsync(CliServices cli, Instance instance, ModMetadata listing, Options options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var repository = options.DryRun ? cli.ReadOnlyUserContent : cli.UserContent;
        var release = options.Version is { } version
            ? await repository.GetReleaseAsync(listing.ModId, ModVersion.Parse(version), cancellationToken).ConfigureAwait(false)
            : await repository.GetLatestReleaseAsync(listing.ModId, options.Channel, cancellationToken).ConfigureAwait(false);
        if (release is null)
            throw new InvalidOperationException(options.Version is null ? $"No release of '{listing.ModId}' in the {options.Channel.ToName()} channel is available." : $"Release {options.Version} of '{listing.ModId}' is not available.");

        var installation = Installation(cli);
        var plan = await PlanAsync(cli, instance, release, options, request => installation.PlanInstallAsync(request, cancellationToken)).ConfigureAwait(false);
        return await RunAsync(cli, instance, installation, plan, options, output, error, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> UpdateAsync(CliServices cli, Instance instance, InstalledContentRecord installed, Options options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var repository = options.DryRun ? cli.ReadOnlyUserContent : cli.UserContent;
        ModVersionMetadata? release;
        if (options.Version is { } version)
        {
            // An exact version may be older, because a pack can pin an older release than the installed one.
            release = await repository.GetReleaseAsync(installed.ListingId, ModVersion.Parse(version), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Release {version} of '{installed.ListingId}' is not available.");
        }
        else
        {
            release = await repository.GetLatestReleaseAsync(installed.ListingId, options.Channel, cancellationToken).ConfigureAwait(false);
        }

        if (release is null || (options.Version is null ? release.Version <= installed.Version : release.Version == installed.Version))
        {
            if (options.Json)
                JsonOutput.Write(output, new UserContentView(installed.ListingId, ContentOutput.Name(installed.Type), installed.Version.ToString(), installed.FolderName, "up-to-date", [], [], [], [], [], null));
            else if (options.Version is not null)
                output.WriteLine($"{installed.ListingId} {installed.Version} is installed. Nothing to do.");
            else
                output.WriteLine($"{installed.ListingId} {installed.Version} is the newest release in the {options.Channel.ToName()} channel. Nothing to do.");
            return ExitCodes.Done;
        }

        var installation = Installation(cli);
        var plan = await PlanAsync(cli, instance, release, options, request => installation.PlanUpdateAsync(installed, request, options.DiscardChanges, cancellationToken)).ConfigureAwait(false);
        return await RunAsync(cli, instance, installation, plan, options, output, error, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> RemoveAsync(CliServices cli, Instance instance, InstalledContentRecord installed, Options options, TextWriter output, CancellationToken cancellationToken)
    {
        UserContentResult result;
        if (options.DryRun)
        {
            var changes = await cli.InstalledContent.CompareAsync(instance.InstanceId, installed, cancellationToken).ConfigureAwait(false);
            result = !changes.IsUnchanged && !options.DiscardChanges
                ? new UserContentResult(UserContentOutcome.Changed, installed.FolderName, installed, changes)
                : new UserContentResult(UserContentOutcome.Ready, installed.FolderName, installed);
        }
        else
        {
            result = await cli.UserContentInstaller.RemoveAsync(instance.InstanceId, installed, options.DiscardChanges, cancellationToken).ConfigureAwait(false);
        }

        if (options.Json)
            JsonOutput.Write(output, new UserContentView(installed.ListingId, ContentOutput.Name(installed.Type), installed.Version.ToString(), result.FolderName, OutcomeName(result.Outcome), [], [], [], [], [], ChangesView.Of(result.Changes)));
        else if (result.Outcome is UserContentOutcome.Ready or UserContentOutcome.Removed)
            output.WriteLine($"Remove {ContentOutput.Name(installed.Type)} {installed.ListingId} {installed.Version} from {KindFolder(installed.Type)}/{installed.FolderName} in '{instance.Name}'. The folder goes into the backups.");

        return result.Outcome is UserContentOutcome.Ready or UserContentOutcome.Removed ? ExitCodes.Done : Refuse(result, installed.ListingId, installed.Type);
    }

    private static UserContentInstallation Installation(CliServices cli) =>
        new(cli.InstallPlanner, new InstallPlanExecutor(cli.Instances, cli.Installer, cli.Replacer, cli.SpaceCheck), cli.ModState, cli.UserContentInstaller);

    private static async Task<UserContentPlan> PlanAsync(CliServices cli, Instance instance, ModVersionMetadata release, Options options, Func<UserContentRequest, Task<UserContentPlan>> plan)
    {
        var request = new UserContentRequest(instance, release, options.DryRun ? cli.ReadOnlyMods : cli.Mods, cli.InstalledVersion.GetInstalledVersion()?.Version, cli.GamePlatform.Current, Alternatives: options.Alternatives, Channel: options.Channel, Name: options.Name);
        var result = await plan(request).ConfigureAwait(false);
        if (!options.WithRecommended)
            return result;

        var selected = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var added = false;
            foreach (var choice in result.Mods.Choices.Where(choice => choice.Kind == PlanningChoiceKind.Recommendation))
                added |= selected.Add(choice.Key);
            if (!added)
                return result;

            result = await plan(request with { Recommended = selected }).ConfigureAwait(false);
        }
    }

    private static async Task<int> RunAsync(CliServices cli, Instance instance, UserContentInstallation installation, UserContentPlan plan, Options options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var action = plan.Installed is null ? "Install" : "Update";
        if (!options.Json)
        {
            ModInstallCommands.PrintPlan(output, plan.Mods);
            foreach (var id in plan.Enables)
                output.WriteLine($"Enable {id}.");
            if (plan.IsReady)
                output.WriteLine($"{action} {ContentOutput.Name(plan.Release.Type)} {plan.Release.ModId} {plan.Release.Version} into {KindFolder(plan.Release.Type)}/{plan.Check.FolderName} in '{instance.Name}'.");
        }

        var blocked = !plan.IsReady || (plan.Missing.Count > 0 && !options.AllowMissing);
        if (options.DryRun || blocked)
        {
            if (options.Json)
                JsonOutput.Write(output, UserContentView.Of(plan, plan.Check));
            if (plan.Check.Outcome != UserContentOutcome.Ready)
                return Refuse(plan.Check, plan.Release.ModId, plan.Release.Type);
            if (!plan.Mods.IsReady)
                return ExitCodes.Failed;
            if (plan.Missing.Count > 0 && !options.AllowMissing)
            {
                error.WriteLine($"error: '{plan.Release.ModId}' needs mods that Borea cannot install or enable. Run again with --allow-missing to {action.ToLowerInvariant()} it without them.");
                return ExitCodes.Failed;
            }

            return ExitCodes.Done;
        }

        var stop = new InstallStop();
        using var registration = cancellationToken.Register(stop.Request);
        UserContentResult result;
        try
        {
            result = await installation.ExecuteAsync(plan, options.AllowMissing, new InstallProgressOutput(error), stop).ConfigureAwait(false);
        }
        catch (InstallStoppedException stopped)
        {
            error.WriteLine($"Stopped. {stopped.Completed} of {stopped.Total} mods installed.");
            throw;
        }

        if (options.Json)
            JsonOutput.Write(output, UserContentView.Of(plan, result));
        if (!result.IsDone)
            return Refuse(result, plan.Release.ModId, plan.Release.Type);

        if (!options.Json)
        {
            var listing = await ModInstallCommands.FindCachedListingAsync(cli, plan.Release.ModId, cancellationToken).ConfigureAwait(false);
            ContentOutput.WriteSteps(output, $"Install steps for {plan.Release.ModId}:", listing?.Install?.Steps);
        }

        return ExitCodes.Done;
    }

    /// <summary>Says why nothing changed and which option goes on.</summary>
    private static int Refuse(UserContentResult result, string id, ContentType type) => result.Outcome switch
    {
        UserContentOutcome.NameTaken => throw new InvalidOperationException($"{KindFolder(type)} already holds an item named '{result.FolderName}', so the game would show only one of the two. Run again with --name <new name>."),
        UserContentOutcome.InvalidName => throw new InvalidOperationException($"The game would change the name '{result.FolderName}'. Use letters, digits, spaces, '-' and '_'."),
        UserContentOutcome.AlreadyInstalled => throw new InvalidOperationException($"'{id}' is installed in {KindFolder(type)}/{result.FolderName}. Run 'borea update {id}' to change its version."),
        UserContentOutcome.Changed => throw new InvalidOperationException($"Files of {KindFolder(type)}/{result.FolderName} changed since Borea installed it: {string.Join(", ", ChangedFiles(result.Changes!))}. Run again with --discard-changes to go on; the old folder stays in the backups."),
        UserContentOutcome.GameRunning => throw new InvalidOperationException("KSA or StarMap is running, and Borea cannot tell which instance it uses. Close the game first."),
        UserContentOutcome.NotInstalled => throw new InvalidOperationException($"The folder {KindFolder(type)}/{result.FolderName} of '{id}' is gone."),
        _ => throw new InvalidOperationException($"'{id}' was not changed ({result.Outcome})."),
    };

    private static IEnumerable<string> ChangedFiles(InstalledContentChanges changes) =>
        changes.Added.Select(path => path + " (added)").Concat(changes.Removed.Select(path => path + " (removed)")).Concat(changes.Changed.Select(path => path + " (changed)"));

    private static string KindFolder(ContentType type) => type == ContentType.Save ? "saves" : "Vehicles";

    private static string OutcomeName(UserContentOutcome outcome) => outcome switch
    {
        UserContentOutcome.NameTaken => "name-taken",
        UserContentOutcome.InvalidName => "invalid-name",
        UserContentOutcome.AlreadyInstalled => "already-installed",
        UserContentOutcome.GameRunning => "game-running",
        UserContentOutcome.NotInstalled => "not-installed",
        _ => outcome.ToString().ToLowerInvariant(),
    };

    private sealed record UserContentView(
        string Id,
        string Type,
        string Version,
        string? Folder,
        string Outcome,
        IReadOnlyList<OperationView> Operations,
        IReadOnlyList<string> Enables,
        IReadOnlyList<MessageView> Missing,
        IReadOnlyList<MessageView> Warnings,
        IReadOnlyList<MessageView> Conflicts,
        ChangesView? Changes)
    {
        public static UserContentView Of(UserContentPlan plan, UserContentResult result) => new(
            plan.Release.ModId,
            ContentOutput.Name(plan.Release.Type),
            plan.Release.Version.ToString(),
            result.FolderName,
            !plan.Mods.IsReady ? "conflict" : OutcomeName(result.Outcome),
            plan.Mods.Operations.Select(operation => new OperationView(operation.Release.ModId, operation.Release.Version.ToString())).ToList(),
            plan.Enables,
            plan.Missing.Select(MessageView.Of).ToList(),
            plan.Mods.Warnings.Except(plan.Missing).Select(MessageView.Of).ToList(),
            [.. plan.Mods.Conflicts.Select(MessageView.Of), .. plan.Mods.UnresolvedChoices.Select(MessageView.Of)],
            ChangesView.Of(result.Changes));
    }

    private sealed record OperationView(string Id, string Version);

    /// <param name="Value">For a missing mod, the code of the reason it cannot be installed.</param>
    private sealed record MessageView(string Id, string Code, string Message, string? Dependency, string? Value)
    {
        public static MessageView Of(PlanningMessage message) => new(message.ModId, message.Code, message.Message, message.Dependency?.ModId, message.Value);
    }

    private sealed record ChangesView(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
    {
        public static ChangesView? Of(InstalledContentChanges? changes) => changes is null ? null : new(changes.Added, changes.Removed, changes.Changed);
    }
}
