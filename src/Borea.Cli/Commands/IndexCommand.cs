using System.CommandLine;
using Borea.Core.Index;

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
            var result = await cli.IndexReader.ReadAsync(ct).ConfigureAwait(false);
            var unknownListings = CountDiagnostics(result, ContentIndexDiagnosticScope.Listing, ContentIndexDiagnosticKind.UnsupportedVersion);
            var malformedListings = CountDiagnostics(result, ContentIndexDiagnosticScope.Listing, ContentIndexDiagnosticKind.Malformed);
            var unknownReleases = CountDiagnostics(result, ContentIndexDiagnosticScope.Release, ContentIndexDiagnosticKind.UnsupportedVersion);
            var rejectedReleases = CountDiagnostics(result, ContentIndexDiagnosticScope.Release, ContentIndexDiagnosticKind.Malformed);
            var unknownPacks = CountDiagnostics(result, ContentIndexDiagnosticScope.Pack, ContentIndexDiagnosticKind.UnsupportedVersion);
            var malformedPacks = CountDiagnostics(result, ContentIndexDiagnosticScope.Pack, ContentIndexDiagnosticKind.Malformed);
            var unknownVersions = CountDiagnostics(result, ContentIndexDiagnosticScope.PackVersion, ContentIndexDiagnosticKind.UnsupportedVersion);
            var rejectedVersions = CountDiagnostics(result, ContentIndexDiagnosticScope.PackVersion, ContentIndexDiagnosticKind.Malformed);

            output.WriteLine($"""
                Index Validated:
                  Spec Version: {result.SnapshotVersion}
                  {result.Listings.Count} valid, {unknownListings} unknown, {malformedListings} malformed Listings
                    {result.Listings.Sum(listing => listing.Releases.Count)} valid, {unknownReleases} unknown, {rejectedReleases} rejected Releases
                  {result.Packs.Count} valid, {unknownPacks} unknown, {malformedPacks} malformed Mod Packs
                    {result.Packs.Sum(pack => pack.Versions.Count)} valid, {unknownVersions} unknown, {rejectedVersions} rejected Releases
                  {result.GameVersions?.Versions.Count ?? 0} known Game Versions
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
            var result = await cli.IndexReader.ReadAsync(ct).ConfigureAwait(false);
            output.WriteLine($"Mapped {result.Listings.Count} listings and {result.Packs.Count} packs.");
            return ExitCodes.Done;
        }));

        return map;
    }

    private static int CountDiagnostics(
        ContentIndexSnapshot snapshot,
        ContentIndexDiagnosticScope scope,
        ContentIndexDiagnosticKind kind) =>
        snapshot.Diagnostics.Count(diagnostic => diagnostic.Scope == scope && diagnostic.Kind == kind);
}
