using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

internal static class ShowCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = ArgumentRules.ContentId("id", "The listing id.");
        var version = VersionOption();
        var json = ArgumentRules.Json();
        var show = new Command("show", "Show one listing or one of its releases.");
        show.Arguments.Add(id);
        show.Options.Add(version);
        show.Options.Add(json);

        show.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var listingId = parseResult.GetRequiredValue(id);
            var requestedText = parseResult.GetValue(version);
            var requestedVersion = requestedText is null ? (ModVersion?)null : ModVersion.Parse(requestedText);
            var requestedCanonical = requestedVersion?.ToString();
            var available = await cli.Mods.GetAvailableModsAsync(ct).ConfigureAwait(false);
            var repositoryListing = available.FirstOrDefault(listing => ModIds.Equals(listing.ModId, listingId));
            var repositoryRelease = requestedVersion is null
                ? null
                : await cli.Mods.GetReleaseAsync(listingId, requestedVersion.Value, ct).ConfigureAwait(false);
            var snapshot = await cli.IndexSnapshots.GetSnapshotAsync(ct).ConfigureAwait(false);
            var indexListing = snapshot.Listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, listingId));
            var metadata = repositoryListing ?? indexListing?.Authored;
            var diagnostics = MatchingDiagnostics(snapshot, listingId, requestedCanonical);
            var hasUnknownListing = diagnostics.Any(diagnostic =>
                diagnostic.Kind == "unsupported-version" && diagnostic.Scope == "listing");

            if (metadata is null && indexListing is null && !hasUnknownListing)
                throw new InvalidOperationException($"Listing '{listingId}' was not found.");

            IReadOnlyList<ModVersionMetadata> releases;
            if (requestedVersion is { } exact)
            {
                var release = repositoryRelease
                    ?? indexListing?.Releases.FirstOrDefault(candidate => candidate.Version.Equals(exact));
                var hasUnknownRelease = diagnostics.Any(diagnostic =>
                    diagnostic.Kind == "unsupported-version"
                    && diagnostic.Scope == "release"
                    && string.Equals(diagnostic.Version, requestedCanonical, StringComparison.OrdinalIgnoreCase));

                if (release is null && metadata is not null && !hasUnknownRelease)
                    throw new InvalidOperationException($"Release '{listingId}' {exact} was not found.");

                releases = release is null ? Array.Empty<ModVersionMetadata>() : new[] { release };
            }
            else if (indexListing is not null)
            {
                releases = indexListing.Releases.OrderByDescending(release => release.Version).ToArray();
            }
            else
            {
                releases = await GetRepositoryReleasesAsync(cli.Mods, listingId, ct).ConfigureAwait(false);
            }

            var installed = cli.InstalledVersion.GetInstalledVersion()?.Version;
            var releaseViews = releases
                .Select(release => ReleaseView.From(release, installed, requestedVersion is not null))
                .Concat(diagnostics
                    .Where(diagnostic => diagnostic.Kind == "unsupported-version")
                    .Where(diagnostic => diagnostic.Scope == "release")
                    .Where(diagnostic => diagnostic.Version is not null)
                    .Where(diagnostic => releases.All(release =>
                        !string.Equals(release.Version.ToString(), diagnostic.Version, StringComparison.OrdinalIgnoreCase)))
                    .Select(ReleaseView.Unknown))
                .ToList();
            releaseViews.Sort(CompareReleasesNewestFirst);
            var state = metadata is not null
                ? "known"
                : indexListing?.IndexStatus?.State == IndexStatusState.Delisted
                    ? "delisted"
                    : "unknown";
            var view = new ShowView(
                listingId,
                state,
                installed?.ToString(),
                requestedVersion?.ToString(),
                metadata is null ? null : ListingView.From(metadata),
                ContentOutput.IndexStatus(indexListing?.IndexStatus),
                releaseViews,
                diagnostics);

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, view);
            else
                WriteHuman(output, view);

            return ExitCodes.Done;
        }));

        return show;
    }

    private static Option<string?> VersionOption()
    {
        var version = new Option<string?>("--version") { Description = "Show only this release." };
        version.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (string.IsNullOrWhiteSpace(value) || !ModVersion.TryParse(value, out _))
                result.AddError($"'{value}' is not a valid semantic version.");
        });
        return version;
    }

    private static async Task<IReadOnlyList<ModVersionMetadata>> GetRepositoryReleasesAsync(
        IModRepository repository,
        string id,
        CancellationToken cancellationToken)
    {
        var versions = await repository.GetAvailableVersionsAsync(id, cancellationToken).ConfigureAwait(false);
        var releases = new List<ModVersionMetadata>(versions.Count);
        foreach (var version in versions.OrderByDescending(value => value))
        {
            var release = await repository.GetReleaseAsync(id, version, cancellationToken).ConfigureAwait(false);
            if (release is not null)
                releases.Add(release);
        }
        return releases;
    }

    private static IReadOnlyList<DiagnosticView> MatchingDiagnostics(
        ContentIndexSnapshot snapshot,
        string id,
        string? version)
    {
        return snapshot.Diagnostics
            .Where(diagnostic => ModIds.Equals(diagnostic.Id, id))
            .Where(diagnostic => version is null
                || diagnostic.Scope is ContentIndexDiagnosticScope.Listing or ContentIndexDiagnosticScope.IndexStatus
                || string.Equals(diagnostic.Version, version, StringComparison.OrdinalIgnoreCase))
            .Select(ContentOutput.Diagnostic)
            .ToArray();
    }

    private static void WriteHuman(TextWriter output, ShowView view)
    {
        output.WriteLine(view.Listing is null
            ? $"{view.Id} ({view.State})"
            : $"{view.Listing.Name} ({view.Id})");

        if (view.Listing is { } listing)
        {
            output.WriteLine($"Type: {listing.Type}");
            output.WriteLine($"Source: {listing.Source}");
            output.WriteLine($"Authors: {string.Join(", ", listing.Authors)}");
            output.WriteLine($"License: {listing.License}");
            output.WriteLine($"Status: {listing.Status}");
            if (listing.SupersededBy is not null)
                output.WriteLine($"Superseded by: {listing.SupersededBy}");
            output.WriteLine(listing.Abstract);
            if (!string.IsNullOrWhiteSpace(listing.Description))
                output.WriteLine(listing.Description);
            if (listing.Tags.Count > 0)
                output.WriteLine($"Tags: {string.Join(", ", listing.Tags)}");
            output.WriteLine("Links:");
            foreach (var link in listing.Links)
                output.WriteLine($"  {link.Key}: {link.Value}");
        }

        if (view.IndexStatus is { } status)
        {
            var since = status.Since is null ? string.Empty : $" since {status.Since:O}";
            output.WriteLine($"Index status: {status.State}{since}");
            if (status.Reason is not null)
                output.WriteLine($"Index reason: {status.Reason}");
        }

        if (view.Releases.Count == 0)
        {
            output.WriteLine(view.RequestedVersion is null
                ? "Releases: none"
                : $"Release {view.RequestedVersion}: unknown");
        }
        else
        {
            output.WriteLine(view.RequestedVersion is null ? "Releases:" : "Release:");
            foreach (var release in view.Releases)
            {
                if (release.State == "unknown")
                {
                    var specVersion = release.SpecVersion is null ? string.Empty : $" (spec version {release.SpecVersion})";
                    output.WriteLine($"  {release.Version}  unknown{specVersion}: {release.Reason}");
                    continue;
                }

                var yanked = release.Yanked
                    ? release.YankedReason is null ? "  yanked" : $"  yanked: {release.YankedReason}"
                    : string.Empty;
                output.WriteLine($"  {release.Version}  {release.Compatibility}  {release.ReleaseStatus}{yanked}");
                output.WriteLine($"    Game: {release.GameMin} to {release.GameMax ?? "open"}");
                if (release.Dependencies is not null)
                {
                    output.WriteLine(release.Dependencies.Count == 0 ? "    Dependencies: none" : "    Dependencies:");
                    foreach (var dependency in release.Dependencies)
                        output.WriteLine($"      {Describe(dependency)}");
                }
            }
        }

        ContentOutput.WriteDiagnostics(output, view.Diagnostics);
    }

    private static string Describe(DependencyView dependency)
    {
        var target = dependency.Id ?? $"any of [{string.Join(", ", dependency.AnyOf!.Select(Describe))}]";
        var bounds = Bounds(dependency.MinVersion, dependency.MaxVersion);
        var source = dependency.Source is null ? string.Empty : $" ({dependency.Source})";
        return $"{dependency.Kind}  {target}{bounds}{source}";
    }

    private static string Describe(DependencyAlternativeView alternative) =>
        $"{alternative.Id}{Bounds(alternative.MinVersion, alternative.MaxVersion)}";

    private static string Bounds(string? min, string? max) => (min, max) switch
    {
        (null, null) => string.Empty,
        (not null, null) => $" >= {min}",
        (null, not null) => $" <= {max}",
        _ => $" >= {min} <= {max}",
    };

    private static int CompareReleasesNewestFirst(ReleaseView left, ReleaseView right)
    {
        var leftParsed = ModVersion.TryParse(left.Version, out var leftVersion);
        var rightParsed = ModVersion.TryParse(right.Version, out var rightVersion);
        if (leftParsed && rightParsed)
        {
            var precedence = rightVersion.CompareTo(leftVersion);
            if (precedence != 0)
                return precedence;
        }
        else if (leftParsed != rightParsed)
        {
            return leftParsed ? -1 : 1;
        }

        return StringComparer.Ordinal.Compare(left.Version, right.Version);
    }

    private sealed record ShowView(
        string Id,
        string State,
        string? InstalledGameVersion,
        string? RequestedVersion,
        ListingView? Listing,
        IndexStatusView? IndexStatus,
        IReadOnlyList<ReleaseView> Releases,
        IReadOnlyList<DiagnosticView> Diagnostics);

    private sealed record ListingView(
        int SpecVersion,
        string Id,
        string Type,
        string Source,
        string Name,
        IReadOnlyList<string> Authors,
        string Abstract,
        string? Description,
        string License,
        IReadOnlyList<string> Tags,
        string Status,
        string? SupersededBy,
        IReadOnlyDictionary<string, string> Links)
    {
        public static ListingView From(ModMetadata listing) => new(
            listing.SpecVersion,
            listing.ModId,
            ContentOutput.Name(listing.Type),
            listing.Source,
            listing.Name,
            listing.Authors,
            listing.Abstract,
            listing.Description,
            listing.License,
            listing.Tags,
            ContentOutput.Name(listing.Status),
            listing.SupersededBy,
            new SortedDictionary<string, string>(
                listing.Links.ToDictionary(link => link.Key, link => link.Value),
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed record ReleaseView(
        string State,
        int? SpecVersion,
        string Version,
        string? ReleaseStatus,
        DateTimeOffset? ReleaseDate,
        string Compatibility,
        string? GameMin,
        int? GameMinRevision,
        string? GameMax,
        int? GameMaxRevision,
        bool Yanked,
        string? YankedReason,
        string? Source,
        IReadOnlyList<DependencyView>? Dependencies,
        string? Reason)
    {
        public static ReleaseView From(ModVersionMetadata release, GameVersion? installed, bool includeDependencies) => new(
            "known",
            release.SpecVersion,
            release.Version.ToString(),
            ContentOutput.Name(release.ReleaseStatus),
            release.ReleaseDate,
            ContentOutput.Name(Borea.Core.Game.Compatibility.Evaluate(release, installed)),
            release.GameMin,
            release.GameMinRevision,
            release.GameMax,
            release.GameMaxRevision,
            release.Yanked,
            release.YankedReason,
            release.Source,
            includeDependencies ? release.Dependencies.Select(DependencyView.From).ToArray() : null,
            null);

        public static ReleaseView Unknown(DiagnosticView diagnostic) => new(
            "unknown",
            diagnostic.SpecVersion,
            diagnostic.Version!,
            null,
            null,
            "unknown",
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            diagnostic.Reason);
    }

    private sealed record DependencyView(
        string Kind,
        string? Id,
        string? MinVersion,
        string? MaxVersion,
        IReadOnlyList<DependencyAlternativeView>? AnyOf,
        string? Source)
    {
        public static DependencyView From(ModDependency dependency) => new(
            ContentOutput.Name(dependency.Kind),
            dependency.ModId,
            dependency.MinVersion?.ToString(),
            dependency.MaxVersion?.ToString(),
            dependency.AnyOf?.Select(DependencyAlternativeView.From).ToArray(),
            ContentOutput.Name(dependency.Source));
    }

    private sealed record DependencyAlternativeView(string Id, string? MinVersion, string? MaxVersion)
    {
        public static DependencyAlternativeView From(ModDependencyAlternative alternative) => new(
            alternative.ModId,
            alternative.MinVersion?.ToString(),
            alternative.MaxVersion?.ToString());
    }
}
