using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

internal static class SearchCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var text = ArgumentRules.Text("text", "Text to find in a listing id, name, abstract, description, or tag.");
        var json = ArgumentRules.Json();
        var search = new Command("search", "Search the cached content index.");
        search.Arguments.Add(text);
        search.Options.Add(json);

        search.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var query = parseResult.GetRequiredValue(text);
            var installed = cli.InstalledVersion.GetInstalledVersion()?.Version;
            var listings = await cli.Mods.SearchAsync(query, ct).ConfigureAwait(false);
            var results = new List<SearchResultView>(listings.Count);

            foreach (var listing in listings)
            {
                var latest = await cli.Mods.GetLatestReleaseAsync(listing.ModId, ct).ConfigureAwait(false);
                results.Add(SearchResultView.From(listing, latest, installed));
            }

            var snapshot = await cli.IndexSnapshots.GetSnapshotAsync(ct).ConfigureAwait(false);
            var resultIds = new HashSet<string>(results.Select(result => result.Id), ModIds.Comparer);
            var unknownListings = snapshot.Diagnostics
                .Where(diagnostic => diagnostic.Kind == ContentIndexDiagnosticKind.UnsupportedVersion)
                .Where(diagnostic => diagnostic.Scope == ContentIndexDiagnosticScope.Listing)
                .Where(diagnostic => diagnostic.Id?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Where(diagnostic => !resultIds.Contains(diagnostic.Id!))
                .OrderBy(diagnostic => diagnostic.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var diagnostic in unknownListings)
            {
                results.Add(new SearchResultView(
                    diagnostic.Id!,
                    null,
                    "unknown",
                    null,
                    null,
                    "unknown",
                    "unknown"));
                resultIds.Add(diagnostic.Id!);
            }

            results.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id));
            var diagnostics = snapshot.Diagnostics
                .Where(diagnostic => diagnostic.Id is not null && resultIds.Contains(diagnostic.Id))
                .Where(diagnostic => diagnostic.Scope is ContentIndexDiagnosticScope.Listing
                    or ContentIndexDiagnosticScope.Release
                    or ContentIndexDiagnosticScope.IndexStatus)
                .Select(ContentOutput.Diagnostic)
                .ToArray();
            var view = new SearchView(query, installed?.ToString(), results, diagnostics);

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, view);
            else
                WriteHuman(output, view);

            return ExitCodes.Done;
        }));

        return search;
    }

    private static void WriteHuman(TextWriter output, SearchView view)
    {
        if (view.Results.Count == 0)
        {
            output.WriteLine($"No listings match '{view.Query}'.");
            return;
        }

        foreach (var result in view.Results)
        {
            var name = result.Name ?? "unknown listing";
            var version = result.LatestVersion ?? "no release";
            output.WriteLine($"{result.Id}  {name}  {version}  {result.Compatibility}");
        }

        ContentOutput.WriteDiagnostics(output, view.Diagnostics);
    }

    private sealed record SearchView(
        string Query,
        string? InstalledGameVersion,
        IReadOnlyList<SearchResultView> Results,
        IReadOnlyList<DiagnosticView> Diagnostics);

    private sealed record SearchResultView(
        string Id,
        string? Name,
        string State,
        string? Type,
        string? Source,
        string? LatestVersion,
        string Compatibility)
    {
        public static SearchResultView From(ModMetadata listing, ModVersionMetadata? latest, GameVersion? installed) => new(
            listing.ModId,
            listing.Name,
            "known",
            ContentOutput.Name(listing.Type),
            listing.Source,
            latest?.Version.ToString(),
            ContentOutput.Name(latest is null ? GameCompatibility.Unknown : Borea.Core.Game.Compatibility.Evaluate(latest, installed)));
    }
}
