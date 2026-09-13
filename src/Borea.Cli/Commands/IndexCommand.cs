using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Index;

namespace Borea.Cli.Commands;

internal static class IndexCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var index = new Command("index", "Manage the cached content index.");
        index.Subcommands.Add(BuildRefresh(services));
        index.Subcommands.Add(BuildValidate(services));
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
        var json = ArgumentRules.Json();
        var validate = new Command("validate", "Validate the cached content index.");
        validate.Options.Add(json);

        validate.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var result = await cli.IndexReader.ReadAsync(ct).ConfigureAwait(false);
            var view = IndexValidationView.From(result);

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, view);
            else
                WriteHuman(output, view);

            return view.Diagnostics.Malformed > 0 ? ExitCodes.Failed : ExitCodes.Done;
        }));

        return validate;
    }

    private static void WriteHuman(TextWriter output, IndexValidationView view)
    {
        output.WriteLine($"Content index validation, snapshot version {view.SnapshotVersion}:");
        output.WriteLine($"  Listings: {view.Accepted.Listings} accepted, {view.Count(ContentIndexDiagnosticScope.Listing, ContentIndexDiagnosticKind.UnsupportedVersion)} unsupported, {view.Count(ContentIndexDiagnosticScope.Listing, ContentIndexDiagnosticKind.Malformed)} malformed.");
        output.WriteLine($"  Releases: {view.Accepted.Releases} accepted, {view.Count(ContentIndexDiagnosticScope.Release, ContentIndexDiagnosticKind.UnsupportedVersion)} unsupported, {view.Count(ContentIndexDiagnosticScope.Release, ContentIndexDiagnosticKind.Malformed)} malformed.");
        output.WriteLine($"  Mod packs: {view.Accepted.Packs} accepted, {view.Count(ContentIndexDiagnosticScope.Pack, ContentIndexDiagnosticKind.UnsupportedVersion)} unsupported, {view.Count(ContentIndexDiagnosticScope.Pack, ContentIndexDiagnosticKind.Malformed)} malformed.");
        output.WriteLine($"  Pack versions: {view.Accepted.PackVersions} accepted, {view.Count(ContentIndexDiagnosticScope.PackVersion, ContentIndexDiagnosticKind.UnsupportedVersion)} unsupported, {view.Count(ContentIndexDiagnosticScope.PackVersion, ContentIndexDiagnosticKind.Malformed)} malformed.");
        output.WriteLine($"  Game versions: {view.Accepted.GameVersions} known, {view.Count(ContentIndexDiagnosticScope.GameVersions, ContentIndexDiagnosticKind.Malformed)} malformed.");
        output.WriteLine($"  Index status: {view.Count(ContentIndexDiagnosticScope.IndexStatus, ContentIndexDiagnosticKind.UnsupportedValue)} unsupported, {view.Count(ContentIndexDiagnosticScope.IndexStatus, ContentIndexDiagnosticKind.Malformed)} malformed.");

        if (view.Diagnostics.Entries.Count == 0)
            return;

        output.WriteLine("Diagnostics:");
        foreach (var diagnostic in view.Diagnostics.Entries)
        {
            var identity = diagnostic.Id is null ? string.Empty : $" {diagnostic.Id}";
            var version = diagnostic.Version is null ? string.Empty : $" {diagnostic.Version}";
            var specVersion = diagnostic.SpecVersion is null ? string.Empty : $" (spec version {diagnostic.SpecVersion})";
            output.WriteLine($"  {diagnostic.Kind} {diagnostic.Scope}{identity}{version}{specVersion}: {diagnostic.Reason}");
        }
    }

    private sealed record IndexValidationView(
        int SnapshotVersion,
        AcceptedContentView Accepted,
        DiagnosticSummaryView Diagnostics)
    {
        public static IndexValidationView From(ContentIndexSnapshot snapshot)
        {
            var entries = snapshot.Diagnostics.Select(DiagnosticView.From).ToArray();
            return new IndexValidationView(
                snapshot.SnapshotVersion,
                new AcceptedContentView(
                    snapshot.Listings.Count,
                    snapshot.Listings.Sum(listing => listing.Releases.Count),
                    snapshot.Packs.Count,
                    snapshot.Packs.Sum(pack => pack.Versions.Count),
                    snapshot.GameVersions?.Versions.Count ?? 0),
                new DiagnosticSummaryView(
                    entries.Count(entry => entry.Kind == "malformed"),
                    entries.Count(entry => entry.Kind == "unsupported-version"),
                    entries.Count(entry => entry.Kind == "unsupported-value"),
                    entries));
        }

        public int Count(ContentIndexDiagnosticScope scope, ContentIndexDiagnosticKind kind) =>
            Diagnostics.Entries.Count(entry => entry.Scope == Name(scope) && entry.Kind == Name(kind));
    }

    private sealed record AcceptedContentView(
        int Listings,
        int Releases,
        int Packs,
        int PackVersions,
        int GameVersions);

    private sealed record DiagnosticSummaryView(
        int Malformed,
        int UnsupportedVersions,
        int UnsupportedValues,
        IReadOnlyList<DiagnosticView> Entries);

    private sealed record DiagnosticView(
        string Kind,
        string Scope,
        string Reason,
        string? Id,
        string? Version,
        int? SpecVersion)
    {
        public static DiagnosticView From(ContentIndexDiagnostic diagnostic) => new(
            Name(diagnostic.Kind),
            Name(diagnostic.Scope),
            diagnostic.Reason,
            diagnostic.Id,
            diagnostic.Version,
            diagnostic.SpecVersion);
    }

    private static string Name(ContentIndexDiagnosticKind kind) => kind switch
    {
        ContentIndexDiagnosticKind.Malformed => "malformed",
        ContentIndexDiagnosticKind.UnsupportedVersion => "unsupported-version",
        ContentIndexDiagnosticKind.UnsupportedValue => "unsupported-value",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string Name(ContentIndexDiagnosticScope scope) => scope switch
    {
        ContentIndexDiagnosticScope.Listing => "listing",
        ContentIndexDiagnosticScope.Release => "release",
        ContentIndexDiagnosticScope.Pack => "pack",
        ContentIndexDiagnosticScope.PackVersion => "pack-version",
        ContentIndexDiagnosticScope.IndexStatus => "index-status",
        ContentIndexDiagnosticScope.GameVersions => "game-versions",
        ContentIndexDiagnosticScope.Tags => "tags",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
