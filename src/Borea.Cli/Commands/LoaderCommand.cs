using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Cli.Commands;

internal static class LoaderCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var loader = new Command("loader", "Install, adopt, and uninstall mod loaders.");
        loader.Subcommands.Add(BuildListInstallableLoaders(services));
        loader.Subcommands.Add(BuildInstall(services));
        loader.Subcommands.Add(BuildAdopt(services));
        loader.Subcommands.Add(BuildUninstall(services));
        return loader;
    }

    private static Command BuildListInstallableLoaders(Func<CancellationToken, Task<CliServices>> services)
    {
        var versionsOption = new Option<bool>("--versions", ["-v"]);
        var installedOption = new Option<bool>("--installed") { Description = "List the installed loader versions and the instances that start each one." };
        var json = ArgumentRules.Json();

        var list = new Command("list", "List installable mod loaders");
        list.Options.Add(versionsOption);
        list.Options.Add(installedOption);
        list.Options.Add(json);
        list.Validators.Add(result =>
        {
            if (result.GetValue(installedOption) && result.GetValue(versionsOption))
                result.AddError("Give --versions or --installed, not both.");
            else if (result.GetValue(json) && !result.GetValue(installedOption))
                result.AddError("--json needs --installed.");
        });

        list.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            if (parseResult.GetValue(installedOption))
                return await ListInstalledAsync(cli, output, parseResult.GetValue(json), ct).ConfigureAwait(false);

            var mods = await cli.Mods.GetAvailableModsAsync(ct).ConfigureAwait(false);
            var modLoaders = mods.Where(c => c.Type == ContentType.ModLoader).ToList();

            if (modLoaders.Count <= 0)
            {
                output.WriteLine("No mod loaders available");
                return ExitCodes.Done;
            }

            foreach (var modloader in modLoaders)
            {
                ct.ThrowIfCancellationRequested();
                output.WriteLine($"{modloader.ModId}");

                var result = parseResult.GetValue(versionsOption);
                if (result)
                {
                    var releases = await cli.Mods.GetReleaseHistoryAsync(modloader.ModId, ct);
                    var versions = releases.Where(v => v.IsOffered).DistinctBy(v => v.Version).OrderByDescending(v => v.Version).ToList();

                    foreach (var version in versions)
                    {
                        output.WriteLine($"  {version.Version}");
                    }
                }
            }

            return ExitCodes.Done;
        }));

        return list;
    }

    private static async Task<int> ListInstalledAsync(CliServices cli, TextWriter output, bool json, CancellationToken cancellationToken)
    {
        var installed = await cli.LoaderVersions.ListAsync(cancellationToken).ConfigureAwait(false);
        var uses = await UsesAsync(cli, installed, cancellationToken).ConfigureAwait(false);
        if (json)
        {
            JsonOutput.Write(output, uses.Select(InstalledLoaderVersionView.From).ToList());
            return ExitCodes.Done;
        }

        if (uses.Count == 0)
        {
            output.WriteLine("No mod loader versions installed");
            return ExitCodes.Done;
        }

        foreach (var use in uses)
        {
            var version = use.Version;
            var marks = (version.IsRecorded ? " (recorded)" : string.Empty) + (version.IsAdopted ? " (adopted)" : string.Empty);
            output.WriteLine($"{version.LoaderId} {VersionText(version)}{marks}: {version.DirectoryPath}");
            output.WriteLine(use.IsUsed ? $"  used by {DescribeUsers(use.Users)}" : "  not used");
        }

        return ExitCodes.Done;
    }

    /// <summary>
    /// Every installed version with the instances that start it. The inputs are the ones of the launch, so a version that a
    /// launch would start is never reported as unused. When the listings cannot be read, this throws, so a removal stops.
    /// </summary>
    private static async Task<IReadOnlyList<LoaderVersionUse>> UsesAsync(CliServices cli, IReadOnlyList<InstalledLoaderVersion> installed, CancellationToken cancellationToken)
    {
        var instances = await cli.Instances.GetAllAsync().ConfigureAwait(false);
        var releases = await LoaderLookup.HeldReleasesAsync(cli.OfflineContentIndex, cancellationToken).ConfigureAwait(false);
        return await LoaderVersionUse
            .UsedByAsync(instances, installed, cli.OfflineContentIndex, cli.Mods, releases, cli.Settings.ReleaseChannel, instance => LoaderLookup.LoaderGameOf(cli, instance).Version, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string VersionText(InstalledLoaderVersion version) => version.Version?.ToString() ?? "(unknown version)";

    /// <summary>The users as "'Flight Test' (pinned), 'Career' (automatic)".</summary>
    private static string DescribeUsers(IEnumerable<LoaderVersionUser> users)
        => string.Join(", ", users.Select(user => $"'{user.Instance.Name}' ({UserMode(user)})"));

    private static string UserMode(LoaderVersionUser user) => user.IsPinned ? "pinned" : "automatic";

    /// <summary>"starts" for one instance and "start" for more, so the sentence reads right.</summary>
    private static string Starts(LoaderVersionUse use) => use.Users.Count == 1 ? "starts" : "start";

    private static Command BuildInstall(Func<CancellationToken, Task<CliServices>> services)
    {
        var loaderId = ArgumentRules.ContentId("loader-id", "The mod loader's content id.");
        var version = VersionOption("The release to install. The newest usable release in the saved release channel when absent.");
        var directory = new Option<string?>("--directory")
        {
            Description = "A folder outside the Loaders folder of Borea for a single install that each later install replaces. Borea puts each version in its own folder when this option is absent.",
        };
        directory.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string?>()))
                result.AddError("The --directory value cannot be empty.");
        });

        var install = new Command("install", "Install one release of a mod loader and configure it for the game.");
        install.Arguments.Add(loaderId);
        install.Options.Add(version);
        install.Options.Add(directory);

        install.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var id = parseResult.GetRequiredValue(loaderId);
            if (cli.IsGameProcessRunning())
                throw new InvalidOperationException($"Close the game before you install {id}.");

            var listing = await LoaderLookup.GetListingAsync(cli.Mods, id, ct).ConfigureAwait(false);
            var rawVersion = parseResult.GetValue(version);
            var channel = cli.Settings.ReleaseChannel;
            var game = cli.InstalledVersion.GetInstalledVersion()?.Version;
            ModVersionMetadata? release;
            if (rawVersion is null)
            {
                // the newest release the index does not rate incompatible with the installed game
                var newest = await cli.Mods.GetLatestReleaseInChannelAsync(listing.ModId, channel, ct).ConfigureAwait(false);
                release = (await LoaderUpdate.FindAsync(cli.Mods, channel, listing.ModId, installation: null, game, ct).ConfigureAwait(false)).Offer;
                if (newest is not null && release?.Version != newest.Version)
                {
                    if (release is null)
                        throw new InvalidOperationException($"Mod loader '{listing.ModId}' has no release in the {channel.ToName()} channel that supports KSA {game}. Use --version to install one anyway.");

                    error.WriteLine($"warning: {listing.ModId} {newest.Version} does not support KSA {game}; installing {release.Version}.");
                }
            }
            else
            {
                release = await cli.Mods.GetReleaseAsync(listing.ModId, ModVersion.Parse(rawVersion), ct).ConfigureAwait(false);
            }

            if (release is null)
                throw new InvalidOperationException(rawVersion is null
                    ? $"Mod loader '{listing.ModId}' has no release in the {channel.ToName()} channel that Borea can install."
                    : $"Mod loader '{listing.ModId}' has no release '{rawVersion}'.");

            if (release.Yanked)
                throw new InvalidOperationException($"Release {release.Version} of {listing.ModId} is yanked and cannot be installed.");

            if (release.Download.UnavailableSince is { } since)
                error.WriteLine($"warning: {new PlanningMessage(listing.ModId, PlanningMessageKind.Unavailable) { Version = release.Version, Since = since }.Message}");

            // No silent downgrade from a recorded release outside the channel, because the recorded version is the one that
            // an older Borea starts. A version that only a pin uses stays beside the new one, so it does not block the install.
            if (rawVersion is null
                && InstalledLoaderVersion.Of(await cli.LoaderVersions.ListAsync(ct).ConfigureAwait(false), listing.ModId)
                    .FirstOrDefault(installed => installed.IsRecorded)?.Version is { } installedVersion
                && installedVersion.CompareTo(release.Version) > 0
                && await cli.Mods.GetReleaseAsync(listing.ModId, installedVersion, ct).ConfigureAwait(false) is { } installedRelease
                && !channel.Includes(installedRelease.ReleaseStatus))
                throw new InvalidOperationException($"{listing.ModId} {installedVersion} is installed, which is newer than {release.Version}, the newest release in the {channel.ToName()} channel{(game is null ? "" : $" that supports KSA {game}")}. Use --version to install an older release.");

            var rawDirectory = parseResult.GetValue(directory);
            var destination = rawDirectory is null ? null : Path.GetFullPath(rawDirectory);
            var replacesUnfitRecord = await LoaderUpdate.ReplacesUnfitRecordAsync(cli.Mods, cli.Settings.LoaderInstallations.GetValueOrDefault(listing.ModId), release, game, ct).ConfigureAwait(false);
            var result = await cli.LoaderInstaller.InstallAsync(listing, release, destination, replacesUnfitRecord, new InstallProgressOutput(error), ct).ConfigureAwait(false);

            output.WriteLine($"Installed {result.LoaderId} {result.Version} in '{result.Directory}'.");
            if (result.ConfigurationFile is not null)
                output.WriteLine($"Configured game directory in '{result.ConfigurationFile}'.");
            ContentOutput.WriteSteps(output, $"Install steps for {listing.ModId}:", listing.Install?.Steps);
            return ExitCodes.Done;
        }));

        return install;
    }

    private static Command BuildAdopt(Func<CancellationToken, Task<CliServices>> services)
    {
        var loaderId = ArgumentRules.ContentId("loader-id", "The mod loader's content id.");
        var directory = ArgumentRules.Text("directory", "The directory that holds the loader's launch file.");
        var adopt = new Command("adopt", "Validate and record a mod loader that Borea did not install.");
        adopt.Arguments.Add(loaderId);
        adopt.Arguments.Add(directory);

        adopt.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, (cli, output, error, ct) =>
            AdoptAsync(
                cli,
                parseResult.GetRequiredValue(loaderId),
                Path.GetFullPath(parseResult.GetRequiredValue(directory)),
                output,
                error,
                ct)));

        return adopt;
    }

    private static Command BuildUninstall(Func<CancellationToken, Task<CliServices>> services)
    {
        var loaderId = ArgumentRules.ContentId("loader-id", "The mod loader's content id.");
        var version = VersionOption("Remove only this installed version. Borea refuses a version that an instance starts.");
        var unused = new Option<bool>("--unused") { Description = "Remove every version that Borea installed and no instance starts." };
        var uninstall = new Command("uninstall", "Remove the recorded version of a mod loader and delete its directory when Borea owns it, or remove one version or the unused versions. Without an option, the other versions stay.");
        uninstall.Arguments.Add(loaderId);
        uninstall.Options.Add(version);
        uninstall.Options.Add(unused);
        uninstall.Validators.Add(result =>
        {
            if (result.GetValue(version) is not null && result.GetValue(unused))
                result.AddError("Give --version or --unused, not both.");
        });

        uninstall.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var id = parseResult.GetRequiredValue(loaderId);
            if (cli.IsGameProcessRunning())
                throw new InvalidOperationException($"Close the game before you remove {id}.");

            if (parseResult.GetValue(version) is { } rawVersion)
                return await UninstallVersionAsync(cli, id, ModVersion.Parse(rawVersion), output, ct).ConfigureAwait(false);

            if (parseResult.GetValue(unused))
                return await UninstallUnusedAsync(cli, id, output, ct).ConfigureAwait(false);

            // the recorded version gets the same check as one version, so no instance loses the version that it starts
            var installed = await cli.LoaderVersions.ListAsync(ct).ConfigureAwait(false);
            if (InstalledLoaderVersion.Of(installed, id).Any(v => v.IsRecorded)
                && (await UsesAsync(cli, installed, ct).ConfigureAwait(false))
                    .FirstOrDefault(use => use.IsUsed && use.Version.IsRecorded && ModIds.Equals(use.Version.LoaderId, id)) is { } used)
            {
                throw new InvalidOperationException(
                    $"Borea keeps {used.Version.LoaderId} {VersionText(used.Version)}, because {DescribeUsers(used.Users)} {Starts(used)} it. Change their mod loader setting with 'borea instance set-loader' first.");
            }

            var result = await cli.LoaderUninstaller
                .UninstallAsync(id, ct)
                .ConfigureAwait(false);

            // the uninstaller refuses while a game runs and keeps the record, so this must not read as "not recorded"
            if (result.Failure == LoaderUninstallFailure.GameRunning)
            {
                throw new InvalidOperationException($"Close the game before you remove {result.LoaderId}.");
            }

            if (result.Failure == LoaderUninstallFailure.NotInstalled)
            {
                output.WriteLine($"Loader {result.LoaderId} was not recorded.");
                return ExitCodes.Done;
            }

            output.WriteLine(result.DirectoryRemoved
                ? $"Removed loader {result.LoaderId} and its directory '{result.Directory}'."
                : $"Removed loader {result.LoaderId} from Borea. Its directory '{result.Directory}' remains.");

            // the steps of the listing are for the whole loader, so they matter only when no version of it remains
            if (result.RecordRemoved)
            {
                var listing = await ModInstallCommands.FindCachedListingAsync(cli, result.LoaderId, ct).ConfigureAwait(false);
                ContentOutput.WriteSteps(output, $"Uninstall steps for {result.LoaderId}:", listing?.Install?.Uninstall);
            }

            return ExitCodes.Done;
        }));

        return uninstall;
    }

    /// <summary>Removes one version, after the check that no instance starts it.</summary>
    private static async Task<int> UninstallVersionAsync(CliServices cli, string loaderId, ModVersion version, TextWriter output, CancellationToken cancellationToken)
    {
        var installed = await cli.LoaderVersions.ListAsync(cancellationToken).ConfigureAwait(false);
        var uses = await UsesAsync(cli, installed, cancellationToken).ConfigureAwait(false);
        if (uses.FirstOrDefault(use => use.IsUsed && ModIds.Equals(use.Version.LoaderId, loaderId) && use.Version.Version == version) is { } used)
        {
            throw new InvalidOperationException(
                $"Borea keeps {used.Version.LoaderId} {version}, because {DescribeUsers(used.Users)} {Starts(used)} it. Change their mod loader setting with 'borea instance set-loader' first.");
        }

        var result = await cli.LoaderUninstaller.UninstallVersionAsync(loaderId, version, cancellationToken).ConfigureAwait(false);
        await WriteVersionRemovedAsync(cli, result, output, cancellationToken).ConfigureAwait(false);
        return ExitCodes.Done;
    }

    /// <summary>
    /// Removes the versions that Borea installed and no instance starts. The uses are read again after each removal, because
    /// the removal of the recorded version moves the record, and an instance can then start another version.
    /// </summary>
    private static async Task<int> UninstallUnusedAsync(CliServices cli, string loaderId, TextWriter output, CancellationToken cancellationToken)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        List<LoaderVersionUse> uses;
        while (true)
        {
            var installed = await cli.LoaderVersions.ListAsync(cancellationToken).ConfigureAwait(false);
            uses = (await UsesAsync(cli, installed, cancellationToken).ConfigureAwait(false))
                .Where(use => ModIds.Equals(use.Version.LoaderId, loaderId))
                .ToList();
            var next = uses.FirstOrDefault(use => !use.IsUsed && !use.Version.IsAdopted && use.Version.Version is not null && !tried.Contains(use.Version.DirectoryPath));
            if (next is null)
                break;

            tried.Add(next.Version.DirectoryPath);
            var result = await cli.LoaderUninstaller.UninstallVersionAsync(next.Version.LoaderId, next.Version.Version!.Value, cancellationToken).ConfigureAwait(false);
            await WriteVersionRemovedAsync(cli, result, output, cancellationToken).ConfigureAwait(false);
            removed++;
        }

        if (removed == 0)
            output.WriteLine($"No unused version of {loaderId} is installed.");

        foreach (var kept in uses.Where(use => use.IsUsed))
            output.WriteLine($"Kept {kept.Version.LoaderId} {VersionText(kept.Version)}, because {DescribeUsers(kept.Users)} {Starts(kept)} it.");

        return ExitCodes.Done;
    }

    /// <summary>Words the result of a version removal, and throws for a removal that changed nothing.</summary>
    private static async Task WriteVersionRemovedAsync(CliServices cli, LoaderUninstallResult result, TextWriter output, CancellationToken cancellationToken)
    {
        var name = $"{result.LoaderId} {result.Version}";
        switch (result.Failure)
        {
            case LoaderUninstallFailure.GameRunning:
                throw new InvalidOperationException($"Close the game before you remove {name}.");
            case LoaderUninstallFailure.NotInstalled:
                throw new InvalidOperationException($"{name} is not installed.");
        }

        if (!result.DirectoryRemoved)
            output.WriteLine($"Removed {name} from Borea. Its directory '{result.Directory}' remains.");
        else if (result.FolderDeleted)
            output.WriteLine($"Removed {name} and its directory '{result.Directory}'.");
        else
            output.WriteLine($"Removed {name}. A file in '{result.Directory}' was locked, so Borea deletes the rest at the next removal.");

        // the steps of the listing are for the whole loader, so they matter only when no version of it remains
        if (result.RecordRemoved)
        {
            var listing = await ModInstallCommands.FindCachedListingAsync(cli, result.LoaderId, cancellationToken).ConfigureAwait(false);
            ContentOutput.WriteSteps(output, $"Uninstall steps for {result.LoaderId}:", listing?.Install?.Uninstall);
        }
    }

    internal static async Task<int> AdoptAsync(
        CliServices services,
        string loaderId,
        string directory,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var listing = await LoaderLookup.GetListingAsync(services.Mods, loaderId, cancellationToken).ConfigureAwait(false);
        // the history keeps a release whose download is gone, so an installed copy of it still gets its version
        var releases = await services.Mods.GetReleaseHistoryAsync(listing.ModId, cancellationToken).ConfigureAwait(false);
        var result = await services.LoaderAdopter
            .AdoptAsync(listing, releases, directory, cancellationToken)
            .ConfigureAwait(false);

        output.WriteLine($"Adopted {result.LoaderId} in '{result.Directory}'.");
        output.WriteLine(result.Version is null
            ? "Loader version: unknown"
            : $"Loader version: {result.Version}");
        foreach (var warning in result.Warnings)
            error.WriteLine($"warning: {warning}");

        return ExitCodes.Done;
    }

    private static Option<string?> VersionOption(string description)
    {
        var version = new Option<string?>("--version")
        {
            Description = description,
        };
        version.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (string.IsNullOrWhiteSpace(value) || !ModVersion.TryParse(value, out _))
                result.AddError($"'{value}' is not a valid semantic version.");
        });
        return version;
    }

    /// <summary>The JSON shape of one entry of <c>loader list --installed</c>.</summary>
    private sealed record InstalledLoaderVersionView(string Id, string? Version, string Directory, bool Adopted, bool Recorded, IReadOnlyList<LoaderVersionUserView> UsedBy)
    {
        public static InstalledLoaderVersionView From(LoaderVersionUse use)
            => new(
                use.Version.LoaderId,
                use.Version.Version?.ToString(),
                use.Version.DirectoryPath,
                use.Version.IsAdopted,
                use.Version.IsRecorded,
                use.Users.Select(user => new LoaderVersionUserView(user.Instance.InstanceId, user.Instance.Name, UserMode(user))).ToList());
    }

    private sealed record LoaderVersionUserView(Guid Id, string Name, string Mode);
}
