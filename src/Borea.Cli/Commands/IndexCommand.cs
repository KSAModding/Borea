using System.CommandLine;
using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Storage.Index;

namespace Borea.Cli.Commands;

internal static class IndexCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var index = new Command("index", "Manage the cached content index.");
        index.Subcommands.Add(BuildRefresh(services));
        index.Subcommands.Add(BuildValidate(services));
        index.Subcommands.Add(BuildMap(services));
        return index;
    }

    private static Command BuildRefresh(Func<CancellationToken, Task<CliServices>> services)
    {
        var refresh = new Command("refresh", "Download the content index when it changed.");

        refresh.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var result = await cli.IndexFetcher.FetchAsync(cli.Paths.GetIndexPath(), ct).ConfigureAwait(false);
            output.WriteLine(result is ContentIndexFetchResult.Downloaded ? "Content index: downloaded." : "Content index: not modified.");
            return ExitCodes.Done;
        }));

        return refresh;
    }

    private static Command BuildValidate(Func<CancellationToken, Task<CliServices>> services)
    {
        var validate = new Command("validate", "Validate the content index and check what content is valid, unknown, or malformed");

        validate.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var validator = new IndexValidator(cli.Paths);
            var result = validator.ValidateIndex();

            int validReleases = 0;
            int unknownReleases = 0;
            int rejectedReleases = 0;
            foreach (var listing in result.ValidListings)
            {
                validReleases += listing.ValidReleases.Count;
                unknownReleases += listing.UnknownReleases.Count;
                rejectedReleases += listing.RejectedReleases.Count;
            }

            int validVersions = 0;
            int unknownVersions = 0;
            int rejectedVersions = 0;
            foreach (var pack in result.ValidPacks)
            {
                validVersions += pack.ValidVersions.Count;
                unknownVersions += pack.UnknownVersions.Count;
                rejectedVersions += pack.RejectedVersions.Count;
            }

            output.WriteLine($"""
                Index Validated:
                  Spec Version: {result.SnapshotVersion}
                  {result.ValidListings.Count} valid, {result.UnknownListings.Count} unknown, {result.MalformedListings.Count} malformed Listings
                    {validReleases} valid, {unknownReleases} unknown, {rejectedReleases} rejected Releases
                  {result.ValidPacks.Count} valid, {result.UnknownPacks.Count} unknown, {result.MalformedPacks.Count} malformed Mod Packs
                    {validVersions} valid, {unknownVersions} unknown, {rejectedVersions} rejected Releases
                  {result.GameVersions.Versions.Count} known Game Versions
                """);
            return ExitCodes.Done;
        }));

        return validate;
    }

    private static Command BuildMap(Func<CancellationToken, Task<CliServices>> services)
    {
        var map = new Command("map", "Map the validated DTOs to the corresponding Borea.Core objects");

        map.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var validator = new IndexValidator(cli.Paths);
            var validateResult = validator.ValidateIndex();

            foreach (var listing in validateResult.ValidListings)
            {
                ModMetadata? authored = null;
                if (listing.Authored is not null)
                    authored = DtoMapper.MapAuthored(listing.Authored, "index");

                IndexStatus? indexStatus = null;
                if (listing.IndexStatus is not null)
                    indexStatus = DtoMapper.MapIndexStatus(listing.IndexStatus);

                foreach (var release in listing.ValidReleases)
                {
                    DtoMapper.MapRelease(release, "index", authored);
                }
            }

            foreach (var pack in validateResult.ValidPacks)
            {
                foreach (var release in pack.ValidVersions)
                {
                    ModPackMetadata? modPack = null;
                    if (release.Authored is not null)
                        modPack = DtoMapper.MapPackVersion(release.Authored, "index");

                    IndexStatus? indexStatus = null;
                    if (release.IndexStatus is not null)
                        indexStatus = DtoMapper.MapIndexStatus(release.IndexStatus);
                }
            }

            output.WriteLine("If you see this, no error was thrown during mapping");
            return ExitCodes.Done;
        }));

        return map;
    }
}
