using System.CommandLine;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

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

        var list = new Command("list", "List installable mod loaders");
        list.Options.Add(versionsOption);

        list.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
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
                    var releases = await LoaderLookup.GetReleasesAsync(cli.Mods, modloader.ModId, ct);
                    var versions = releases.Where(v => !v.Yanked).DistinctBy(v => v.Version).OrderByDescending(v => v.Version).ToList();

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

    private static Command BuildInstall(Func<CancellationToken, Task<CliServices>> services)
    {
        var loaderId = ArgumentRules.ContentId("loader-id", "The mod loader's content id.");
        var version = VersionOption();
        var directory = new Option<string?>("--directory")
        {
            Description = "The standalone install directory. Borea chooses one when this option is absent.",
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

        install.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var id = parseResult.GetRequiredValue(loaderId);
            var listing = await LoaderLookup.GetListingAsync(cli.Mods, id, ct).ConfigureAwait(false);
            var rawVersion = parseResult.GetValue(version);
            var release = rawVersion is null
                ? await cli.Mods.GetLatestReleaseAsync(listing.ModId, ct).ConfigureAwait(false)
                : await cli.Mods.GetReleaseAsync(listing.ModId, ModVersion.Parse(rawVersion), ct).ConfigureAwait(false);

            if (release is null)
                throw new InvalidOperationException(rawVersion is null
                    ? $"Mod loader '{listing.ModId}' has no release that Borea can install."
                    : $"Mod loader '{listing.ModId}' has no release '{rawVersion}'.");

            if (release.Yanked)
                throw new InvalidOperationException($"Release {release.Version} of {listing.ModId} is yanked and cannot be installed.");

            var rawDirectory = parseResult.GetValue(directory);
            var destination = rawDirectory is null ? null : Path.GetFullPath(rawDirectory);
            var result = await cli.LoaderInstaller.InstallAsync(listing, release, destination, cancellationToken: ct).ConfigureAwait(false);

            output.WriteLine($"Installed {result.LoaderId} {result.Version} in '{result.Directory}'.");
            if (result.ConfigurationFile is not null)
                output.WriteLine($"Configured game directory in '{result.ConfigurationFile}'.");
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
        var uninstall = new Command("uninstall", "Remove a mod loader record and delete its directory when Borea owns it.");
        uninstall.Arguments.Add(loaderId);

        uninstall.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var result = await cli.LoaderUninstaller
                .UninstallAsync(parseResult.GetRequiredValue(loaderId), ct)
                .ConfigureAwait(false);

            if (!result.RecordRemoved)
            {
                output.WriteLine($"Loader {result.LoaderId} was not recorded.");
                return ExitCodes.Done;
            }

            output.WriteLine(result.DirectoryRemoved
                ? $"Removed loader {result.LoaderId} and its directory '{result.Directory}'."
                : $"Removed loader {result.LoaderId} from Borea. Its directory '{result.Directory}' remains.");
            return ExitCodes.Done;
        }));

        return uninstall;
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
        var releases = await LoaderLookup.GetReleasesAsync(services.Mods, listing.ModId, cancellationToken).ConfigureAwait(false);
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

    private static Option<string?> VersionOption()
    {
        var version = new Option<string?>("--version")
        {
            Description = "The release to install. The newest usable release when absent.",
        };
        version.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (string.IsNullOrWhiteSpace(value) || !ModVersion.TryParse(value, out _))
                result.AddError($"'{value}' is not a valid semantic version.");
        });
        return version;
    }
}
