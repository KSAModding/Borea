using System.CommandLine;
using Borea.Cli.Output;
using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Tags;

namespace Borea.Cli.Commands;

/// <summary>
/// The pack command group. Search and show read mod packs from the content index,
/// and install gives one exact pack version to <see cref="IModPackInstaller"/>.
/// </summary>
internal static class PackCommand
{
    public static Command Build(Func<CancellationToken, Task<CliServices>> services)
    {
        var pack = new Command("pack", "Find, inspect, and install mod packs.");
        pack.Subcommands.Add(BuildSearch(services));
        pack.Subcommands.Add(BuildShow(services));
        pack.Subcommands.Add(BuildInstall(services));
        return pack;
    }

    private static Command BuildSearch(Func<CancellationToken, Task<CliServices>> services)
    {
        var text = ArgumentRules.Text("text", "Text to find in a pack id, name, abstract, description, author, or tag.");
        var json = ArgumentRules.Json();
        var search = new Command("search", "Search the mod packs in the content index.");
        search.Arguments.Add(text);
        search.Options.Add(json);

        search.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var query = parseResult.GetRequiredValue(text);
            var installed = cli.InstalledVersion.GetInstalledVersion()?.Version;
            var matches = await cli.ModPacks.SearchAsync(query, ct).ConfigureAwait(false);
            var snapshot = await cli.IndexSnapshots.GetSnapshotAsync(ct).ConfigureAwait(false);
            var releases = GameReleaseList.From(snapshot.GameVersions);
            var results = matches
                .Where(match => match.Metadata is not null)
                .Select(match => SearchResultView.Usable(match.Metadata!, installed, releases))
                .ToList();
            var resultIds = new HashSet<string>(results.Select(result => result.Id), ModIds.Comparer);

            // The repository only returns packs with a usable version, so a removed pack,
            // a pack whose every version is retracted, and a pack in a newer format come
            // from the snapshot. They stay visible instead of disappearing from the result.
            foreach (var entry in snapshot.Packs.Where(entry => !resultIds.Contains(entry.Id)))
            {
                if (entry.IndexStatus?.State == IndexStatusState.Delisted)
                {
                    if (entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                        results.Add(SearchResultView.Identity(entry.Id, "delisted"));
                    continue;
                }

                var newest = Newest(entry);
                if (newest is not null && ContentTagFilter.MatchesSearch(newest.Metadata, query))
                    results.Add(SearchResultView.WithoutUsableVersion(newest.Metadata));
            }

            resultIds.UnionWith(results.Select(result => result.Id));
            var unknownIds = snapshot.Diagnostics
                .Where(diagnostic => diagnostic.Kind == ContentIndexDiagnosticKind.UnsupportedVersion)
                .Where(diagnostic => diagnostic.Scope is ContentIndexDiagnosticScope.Pack or ContentIndexDiagnosticScope.PackVersion)
                .Where(diagnostic => diagnostic.Id?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Select(diagnostic => diagnostic.Id!)
                .Where(id => !resultIds.Contains(id))
                .Distinct(ModIds.Comparer)
                .ToArray();

            foreach (var id in unknownIds)
            {
                results.Add(SearchResultView.Identity(id, "unknown"));
                resultIds.Add(id);
            }

            results.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id));
            var diagnostics = snapshot.Diagnostics
                .Where(diagnostic => diagnostic.Id is not null && resultIds.Contains(diagnostic.Id))
                .Where(diagnostic => diagnostic.Scope is ContentIndexDiagnosticScope.Pack
                    or ContentIndexDiagnosticScope.PackVersion
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

    private static Command BuildShow(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = ArgumentRules.ContentId("id", "The pack id.");
        var version = VersionOption("Show only this pack version and the content it pins.");
        var json = ArgumentRules.Json();
        var show = new Command("show", "Show one mod pack or one of its versions.");
        show.Arguments.Add(id);
        show.Options.Add(version);
        show.Options.Add(json);

        show.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var packId = parseResult.GetRequiredValue(id);
            var requestedText = parseResult.GetValue(version);
            var requested = requestedText is null ? (ModVersion?)null : ModVersion.Parse(requestedText);
            var requestedCanonical = requested?.ToString();
            var identity = await cli.ModPacks.GetAsync(packId, ct).ConfigureAwait(false);
            var snapshot = await cli.IndexSnapshots.GetSnapshotAsync(ct).ConfigureAwait(false);
            var entry = snapshot.Packs.FirstOrDefault(pack => ModIds.Equals(pack.Id, packId));
            var diagnostics = MatchingDiagnostics(snapshot, packId, requestedCanonical);
            var hasUnknownContent = diagnostics.Any(diagnostic => diagnostic.Kind == "unsupported-version");

            if (identity is null && entry is null && !hasUnknownContent)
                throw new InvalidOperationException($"Pack '{packId}' was not found.");

            // The newest usable version describes the pack. When every version is
            // retracted, the newest retracted document still carries the pack's name.
            var metadata = identity?.Metadata ?? Newest(entry)?.Metadata;
            var installed = cli.InstalledVersion.GetInstalledVersion()?.Version;
            var releases = GameReleaseList.From(snapshot.GameVersions);
            var versions = new List<VersionView>();

            if (requested is { } exact)
            {
                var selected = await cli.ModPacks.GetVersionAsync(packId, exact, ct).ConfigureAwait(false);
                var hasUnknownVersion = diagnostics.Any(diagnostic =>
                    diagnostic.Kind == "unsupported-version"
                    && diagnostic.Scope == "pack-version"
                    && string.Equals(diagnostic.Version, requestedCanonical, StringComparison.OrdinalIgnoreCase));

                if (selected?.Metadata is null && metadata is not null && !hasUnknownVersion)
                    throw new InvalidOperationException($"Pack '{packId}' {exact} was not found.");

                if (selected?.Metadata is { } pinned)
                    versions.Add(await VersionView.WithMembersAsync(pinned, selected.VersionStatus, installed, releases, cli.Mods, ct).ConfigureAwait(false));
            }
            else if (entry is not null)
            {
                versions.AddRange(entry.Versions.Select(candidate => VersionView.From(candidate.Metadata, candidate.IndexStatus, installed, releases)));
            }

            versions.AddRange(diagnostics
                .Where(diagnostic => diagnostic.Kind == "unsupported-version")
                .Where(diagnostic => diagnostic.Scope == "pack-version")
                .Where(diagnostic => diagnostic.Version is not null)
                .Where(diagnostic => versions.All(known =>
                    !string.Equals(known.Version, diagnostic.Version, StringComparison.OrdinalIgnoreCase)))
                .Select(VersionView.Unknown));
            versions.Sort(CompareVersionsNewestFirst);

            var packStatus = entry?.IndexStatus ?? identity?.PackStatus;
            var state = metadata is not null
                ? "known"
                : packStatus?.State == IndexStatusState.Delisted
                    ? "delisted"
                    : "unknown";
            var view = new ShowView(
                identity?.Id ?? entry?.Id ?? packId,
                state,
                installed?.ToString(),
                requestedCanonical,
                metadata is null ? null : PackView.From(metadata),
                ContentOutput.IndexStatus(packStatus),
                versions,
                diagnostics);

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, view);
            else
                WriteHuman(output, view);

            return ExitCodes.Done;
        }));

        return show;
    }

    private static Command BuildInstall(Func<CancellationToken, Task<CliServices>> services)
    {
        var id = ArgumentRules.ContentId("id", "The pack id to install.");
        var version = VersionOption("Install this exact pack version. The newest usable version when absent.");
        var instance = ArgumentRules.Instance();
        var proceedWithRetracted = new Option<bool>("--proceed-with-retracted")
        {
            Description = "Install the selected pack version even though the index retracted it.",
        };
        var proceedWithYanked = new Option<string[]>("--proceed-with-yanked")
        {
            Description = "Install the yanked release this pack pins for this mod id. Repeat it for each yanked member.",
        };
        proceedWithYanked.Validators.Add(result =>
        {
            foreach (var value in result.GetValueOrDefault<string[]>() ?? [])
            {
                if (!ModIds.IsValid(value))
                    result.AddError($"'{value}' is not a valid content id.");
            }
        });
        var json = ArgumentRules.Json();
        var install = new Command("install", "Install the mods one mod pack version pins into an instance.");
        install.Arguments.Add(id);
        install.Options.Add(version);
        install.Options.Add(instance);
        install.Options.Add(proceedWithRetracted);
        install.Options.Add(proceedWithYanked);
        install.Options.Add(json);

        install.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, error, ct) =>
        {
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parseResult.GetValue(instance)).ConfigureAwait(false);
            var packId = parseResult.GetRequiredValue(id);
            var requestedText = parseResult.GetValue(version);
            var requested = requestedText is null ? (ModVersion?)null : ModVersion.Parse(requestedText);
            var selected = requested is { } exact
                ? await cli.ModPacks.GetVersionAsync(packId, exact, ct).ConfigureAwait(false)
                : await cli.ModPacks.GetLatestAsync(packId, ct).ConfigureAwait(false);
            var snapshot = await cli.IndexSnapshots.GetSnapshotAsync(ct).ConfigureAwait(false);

            if (selected?.Metadata is not { } metadata)
            {
                var entry = snapshot.Packs.FirstOrDefault(pack => ModIds.Equals(pack.Id, packId));
                throw new InvalidOperationException(await UnavailableReasonAsync(cli.ModPacks, entry, packId, requested, selected, ct).ConfigureAwait(false));
            }

            // Only an incompatible game blocks, like a mod release (RFC 0017). The pack's
            // own bounds are checked here, because the planner evaluates only the members.
            var installed = cli.InstalledVersion.GetInstalledVersion()?.Version;
            var compatibility = Compatibility.Evaluate(metadata, installed, GameReleaseList.From(snapshot.GameVersions));
            if (compatibility == GameCompatibility.Incompatible)
            {
                throw new InvalidOperationException(
                    $"Pack '{metadata.ModPackId}' {metadata.Version} is incompatible with the installed game {installed}, because it needs game {metadata.GameMin} or newer.");
            }

            var yanked = parseResult.GetValue(proceedWithYanked) ?? [];
            var request = new ModPackInstallRequest(
                target.InstanceId,
                selected,
                cli.Mods,
                installed,
                CurrentPlatform(),
                ProceedWithRetractedPack: parseResult.GetValue(proceedWithRetracted),
                ProceedWithYankedMembers: yanked.Length == 0 ? null : new HashSet<string>(yanked, ModIds.Comparer));
            var result = await cli.ModPackInstaller.InstallAsync(request, ct).ConfigureAwait(false);
            var view = InstallView.From(
                metadata,
                target.Name,
                SelectionWarnings(selected, metadata, compatibility),
                result,
                selected.Diagnostics.Select(ContentOutput.Diagnostic).ToArray());

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, view);
            else
                WriteHuman(output, view);

            if (result.IsComplete)
                return ExitCodes.Done;

            error.WriteLine($"error: {FailureReason(view, request)}");
            return ExitCodes.Failed;
        }));

        return install;
    }

    private static Option<string?> VersionOption(string description)
    {
        var version = new Option<string?>("--version") { Description = description };
        version.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (string.IsNullOrWhiteSpace(value) || !ModVersion.TryParse(value, out _))
                result.AddError($"'{value}' is not a valid semantic version.");
        });
        return version;
    }

    private static ContentIndexPackVersion? Newest(ContentIndexPack? entry) =>
        entry?.Versions.OrderByDescending(candidate => candidate.Metadata.Version).FirstOrDefault();

    /// <summary>
    /// The warnings about the selected pack version itself, none of which blocks: a disputed
    /// or unknown index state, a deprecated or unknown author status, and a game compatibility
    /// that is untested or unknown. A retracted version is the installer's own warning.
    /// </summary>
    private static IReadOnlyList<MessageView> SelectionWarnings(ModPackResult selected, ModPackMetadata pack, GameCompatibility compatibility)
    {
        var id = pack.ModPackId;
        var warnings = new List<MessageView>();
        AddIndexStatusWarning(warnings, id, $"Pack '{id}'", selected.PackStatus);
        AddIndexStatusWarning(warnings, id, $"Pack version {pack.Version}", selected.VersionStatus);

        if (pack.Status == ModStatus.Deprecated)
        {
            warnings.Add(new MessageView(id, "pack-deprecated", pack.SupersededBy is null
                ? $"Pack '{id}' is deprecated."
                : $"Pack '{id}' is deprecated and superseded by '{pack.SupersededBy}'."));
        }
        else if (pack.Status == ModStatus.Unknown)
        {
            warnings.Add(new MessageView(id, "pack-status", $"Pack '{id}' has an author status that this version of Borea does not know."));
        }

        if (compatibility == GameCompatibility.Untested)
            warnings.Add(new MessageView(id, "pack-compatibility", $"Pack '{id}' {pack.Version} is untested with the installed game, because the game is newer than {pack.GameMax}."));
        else if (compatibility == GameCompatibility.Unknown)
            warnings.Add(new MessageView(id, "pack-compatibility", $"The compatibility of pack '{id}' {pack.Version} with the installed game is unknown."));

        return warnings;
    }

    private static void AddIndexStatusWarning(List<MessageView> warnings, string id, string subject, IndexStatus? status)
    {
        var reason = status?.Reason is null ? string.Empty : $" {status.Reason}";
        if (status?.State == IndexStatusState.Disputed)
            warnings.Add(new MessageView(id, "pack-disputed", $"{subject} is disputed.{reason}"));
        else if (status?.State == IndexStatusState.Unknown)
            warnings.Add(new MessageView(id, "pack-index-status", $"{subject} has the index status '{status.RawState}', which this version of Borea does not know.{reason}"));
    }

    private static IReadOnlyList<DiagnosticView> MatchingDiagnostics(ContentIndexSnapshot snapshot, string id, string? version) =>
        snapshot.Diagnostics
            .Where(diagnostic => ModIds.Equals(diagnostic.Id, id))
            .Where(diagnostic => diagnostic.Scope is ContentIndexDiagnosticScope.Pack
                or ContentIndexDiagnosticScope.PackVersion
                or ContentIndexDiagnosticScope.IndexStatus)
            .Where(diagnostic => version is null
                || diagnostic.Scope is ContentIndexDiagnosticScope.Pack or ContentIndexDiagnosticScope.IndexStatus
                || string.Equals(diagnostic.Version, version, StringComparison.OrdinalIgnoreCase))
            .Select(ContentOutput.Diagnostic)
            .ToArray();

    /// <summary>
    /// One sentence that says why no installable pack version was selected, told apart
    /// by what the repository still knows about the id.
    /// </summary>
    private static async Task<string> UnavailableReasonAsync(
        IModPackRepository packs,
        ContentIndexPack? entry,
        string id,
        ModVersion? requested,
        ModPackResult? selected,
        CancellationToken cancellationToken)
    {
        var identity = await packs.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (identity is null && selected is null)
            return $"Pack '{id}' was not found.";

        if ((selected?.PackStatus ?? identity?.PackStatus)?.State == IndexStatusState.Delisted)
            return $"Pack '{id}' was delisted from the content index and cannot be installed.";

        if (selected is not null)
            return $"Pack '{id}' {requested} uses a metadata format that this version of Borea cannot read.";

        // A pack with readable retracted versions still offers --version, even when one of
        // its other versions is in a newer format.
        var unsupported = identity?.Diagnostics
            .Where(diagnostic => diagnostic.Kind == ContentIndexDiagnosticKind.UnsupportedVersion)
            .ToArray() ?? [];
        var hasReadableVersion = entry is not null && entry.Versions.Count > 0;
        if (identity is { Metadata: null }
            && (unsupported.Any(diagnostic => diagnostic.Scope == ContentIndexDiagnosticScope.Pack)
                || (!hasReadableVersion && unsupported.Length > 0)))
            return $"Pack '{id}' uses a metadata format that this version of Borea cannot read.";

        return requested is null
            ? $"Pack '{id}' has no usable version. Pass --version to select a retracted version."
            : $"Pack '{id}' {requested} was not found.";
    }

    /// <summary>
    /// One sentence for an incomplete install. An option is named only for a choice the
    /// caller did not make and that left a member unresolved, because the planner also
    /// warns about yanked releases the caller already accepted.
    /// </summary>
    private static string FailureReason(InstallView view, ModPackInstallRequest request)
    {
        if (!request.ProceedWithRetractedPack && view.Warnings.Any(warning => warning.Code == "retracted-pack"))
            return $"Pack '{view.PackId}' {view.Version} is retracted. Pass --proceed-with-retracted to install it anyway.";

        var unresolved = new HashSet<string>(
            view.Members.Where(member => member.Status == "unresolved").Select(member => member.Id),
            ModIds.Comparer);
        var yanked = view.Warnings
            .Where(warning => warning.Code == "yanked" && unresolved.Contains(warning.Id))
            .Where(warning => request.ProceedWithYankedMembers?.Any(id => ModIds.Equals(id, warning.Id)) != true)
            .Select(warning => warning.Id)
            .Distinct(ModIds.Comparer)
            .ToArray();
        if (yanked.Length > 0)
            return $"The pack pins a yanked release of {string.Join(", ", yanked)}. Pass --proceed-with-yanked with each mod id to install it anyway.";

        var incomplete = view.Members.Count(member => member.Status is not ("installed" or "replaced" or "already-installed"));
        return $"The pack was not installed completely, because {incomplete} of {view.Members.Count} members did not install.";
    }

    private static OsPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? OsPlatform.Windows : OperatingSystem.IsLinux() ? OsPlatform.Linux : OsPlatform.MacOs;

    private static int CompareVersionsNewestFirst(VersionView left, VersionView right)
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

    private static string Name(ModPackMemberStatus status) => status switch
    {
        ModPackMemberStatus.Installed => "installed",
        ModPackMemberStatus.Replaced => "replaced",
        ModPackMemberStatus.AlreadyInstalled => "already-installed",
        ModPackMemberStatus.Unresolved => "unresolved",
        ModPackMemberStatus.Failed => "failed",
        ModPackMemberStatus.NotAttempted => "not-attempted",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static string Name(InstallReason reason) => reason switch
    {
        InstallReason.Manual => "manual",
        InstallReason.ModPack => "modpack",
        InstallReason.Dependency => "dependency",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static void WriteHuman(TextWriter output, SearchView view)
    {
        if (view.Results.Count == 0)
        {
            output.WriteLine($"No packs match '{view.Query}'.");
            return;
        }

        foreach (var result in view.Results)
        {
            var name = result.Name ?? $"{result.State} pack";
            var version = result.LatestVersion ?? (result.State == "known" ? "no usable version" : "unknown");
            output.WriteLine($"{result.Id}  {name}  {version}  {result.Compatibility}");
        }

        ContentOutput.WriteDiagnostics(output, view.Diagnostics);
    }

    private static void WriteHuman(TextWriter output, ShowView view)
    {
        output.WriteLine(view.Pack is null
            ? $"{view.Id} ({view.State})"
            : $"{view.Pack.Name} ({view.Id})");

        if (view.Pack is { } pack)
        {
            output.WriteLine($"Authors: {string.Join(", ", pack.Authors)}");
            output.WriteLine($"License: {pack.License}");
            output.WriteLine($"Status: {pack.Status}");
            if (pack.SupersededBy is not null)
                output.WriteLine($"Superseded by: {pack.SupersededBy}");
            output.WriteLine(pack.Abstract);
            if (!string.IsNullOrWhiteSpace(pack.Description))
                output.WriteLine(pack.Description);
            if (pack.Tags.Count > 0)
                output.WriteLine($"Tags: {string.Join(", ", pack.Tags)}");
            output.WriteLine("Links:");
            foreach (var link in pack.Links)
                output.WriteLine($"  {link.Key}: {link.Value}");
        }

        if (view.IndexStatus is { } status)
        {
            var since = status.Since is null ? string.Empty : $" since {status.Since:O}";
            output.WriteLine($"Index status: {status.State}{since}");
            if (status.Reason is not null)
                output.WriteLine($"Index reason: {status.Reason}");
        }

        if (view.Versions.Count == 0)
        {
            output.WriteLine(view.RequestedVersion is null
                ? "Versions: none"
                : $"Version {view.RequestedVersion}: unknown");
        }
        else
        {
            output.WriteLine(view.RequestedVersion is null ? "Versions:" : "Version:");
            foreach (var version in view.Versions)
                WriteVersion(output, version);
        }

        ContentOutput.WriteDiagnostics(output, view.Diagnostics);
    }

    private static void WriteVersion(TextWriter output, VersionView version)
    {
        if (version.State == "unknown")
        {
            var specVersion = version.SpecVersion is null ? string.Empty : $" (spec version {version.SpecVersion})";
            output.WriteLine($"  {version.Version}  unknown{specVersion}: {version.Reason}");
            return;
        }

        var retracted = version.Retracted
            ? version.IndexStatus?.Reason is null ? "  retracted" : $"  retracted: {version.IndexStatus.Reason}"
            : string.Empty;
        output.WriteLine($"  {version.Version}  {version.Compatibility}  released {version.ReleasedAt:yyyy-MM-dd}{retracted}");
        output.WriteLine($"    Game: {version.GameMin} to {version.GameMax ?? "open"}");

        if (version.Mods is null)
            return;

        if (version.Changelog is not null)
            output.WriteLine($"    Changelog: {version.Changelog}");
        WriteMembers(output, "Mods", version.Mods);
        WriteMembers(output, "Vehicles", version.Vehicles ?? []);
        WriteMembers(output, "Saves", version.Saves ?? []);
    }

    private static void WriteMembers(TextWriter output, string section, IReadOnlyList<MemberView> members)
    {
        output.WriteLine(members.Count == 0 ? $"    {section}: none" : $"    {section}:");
        foreach (var member in members)
        {
            var state = member.State is null ? string.Empty : $"  {member.State}";
            var reason = member.YankedReason is null ? string.Empty : $": {member.YankedReason}";
            output.WriteLine($"      {member.Id} {member.Version}{state}{reason}");
        }
    }

    private static void WriteHuman(TextWriter output, InstallView view)
    {
        output.WriteLine($"Pack {view.PackId} {view.Version} into '{view.InstanceName}':");

        foreach (var warning in view.Warnings)
        {
            output.WriteLine(warning.Code switch
            {
                "retracted-pack" => $"warning: Pack version {view.Version} is retracted. {warning.Message}",
                "yanked" => $"warning: The selected release of {warning.Id} is yanked. {warning.Message}",
                _ when warning.Code.StartsWith("pack-", StringComparison.Ordinal) => $"warning: {warning.Message}",
                _ => $"warning: {warning.Id}: {warning.Message}",
            });
        }

        if (view.Skipped.Count > 0)
        {
            var skipped = string.Join(", ", view.Skipped.Select(entry => $"{entry.Id} {entry.Version}"));
            output.WriteLine($"warning: Borea does not install pinned vehicles and saves yet, so it skipped {skipped}.");
        }

        foreach (var member in view.Members)
        {
            var message = member.Message is null ? string.Empty : $": {member.Message}";
            var location = member.Location is null ? string.Empty : $" Author location: {member.Location}";
            output.WriteLine($"  {member.Status}  {member.Id} {member.Version}  {member.Reason}{message}{location}");
        }

        foreach (var choice in view.UnresolvedChoices)
            output.WriteLine($"choice: {choice.Message}");

        foreach (var conflict in view.Conflicts)
            output.WriteLine($"conflict: {conflict.Message}");

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
        string? LatestVersion,
        string Compatibility)
    {
        public static SearchResultView Usable(ModPackMetadata pack, GameVersion? installed, GameReleaseList releases) => new(
            pack.ModPackId,
            pack.Name,
            "known",
            pack.Version.ToString(),
            ContentOutput.Name(Borea.Core.Game.Compatibility.Evaluate(pack, installed, releases)));

        public static SearchResultView WithoutUsableVersion(ModPackMetadata newest) => new(
            newest.ModPackId,
            newest.Name,
            "known",
            null,
            ContentOutput.Name(GameCompatibility.Unknown));

        public static SearchResultView Identity(string id, string state) => new(
            id,
            null,
            state,
            null,
            ContentOutput.Name(GameCompatibility.Unknown));
    }

    private sealed record ShowView(
        string Id,
        string State,
        string? InstalledGameVersion,
        string? RequestedVersion,
        PackView? Pack,
        IndexStatusView? IndexStatus,
        IReadOnlyList<VersionView> Versions,
        IReadOnlyList<DiagnosticView> Diagnostics);

    private sealed record PackView(
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
        public static PackView From(ModPackMetadata pack) => new(
            pack.SpecVersion,
            pack.ModPackId,
            ContentOutput.Name(pack.Type),
            pack.Source,
            pack.Name,
            pack.Authors,
            pack.Abstract,
            pack.Description,
            pack.License,
            pack.Tags,
            ContentOutput.Name(pack.Status),
            pack.SupersededBy,
            new SortedDictionary<string, string>(
                pack.Links.ToDictionary(link => link.Key, link => link.Value),
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed record VersionView(
        string State,
        int? SpecVersion,
        string Version,
        bool Retracted,
        IndexStatusView? IndexStatus,
        DateTimeOffset? ReleasedAt,
        string Compatibility,
        string? GameMin,
        string? GameMax,
        IReadOnlyList<string>? Os,
        string? Changelog,
        IReadOnlyList<MemberView>? Mods,
        IReadOnlyList<MemberView>? Vehicles,
        IReadOnlyList<MemberView>? Saves,
        string? Reason)
    {
        public static VersionView From(ModPackMetadata pack, IndexStatus? status, GameVersion? installed, GameReleaseList releases) => new(
            "known",
            pack.SpecVersion,
            pack.Version.ToString(),
            status?.State == IndexStatusState.Retracted,
            ContentOutput.IndexStatus(status),
            pack.ReleasedAt,
            ContentOutput.Name(Borea.Core.Game.Compatibility.Evaluate(pack, installed, releases)),
            pack.GameMin,
            pack.GameMax,
            pack.Os,
            pack.Changelog,
            null,
            null,
            null,
            null);

        /// <summary>
        /// The version with its pins. A pinned mod also shows whether the index lists
        /// that exact release and whether it is yanked, because both need a choice at
        /// install time. Vehicles and saves have no content type to look up yet.
        /// </summary>
        public static async Task<VersionView> WithMembersAsync(
            ModPackMetadata pack,
            IndexStatus? status,
            GameVersion? installed,
            GameReleaseList releases,
            IModRepository mods,
            CancellationToken cancellationToken)
        {
            var members = new List<MemberView>(pack.Mods.Count);
            foreach (var pin in pack.Mods)
            {
                var release = await mods.GetReleaseAsync(pin.ContentId, pin.Version, cancellationToken).ConfigureAwait(false);
                members.Add(new MemberView(
                    pin.ContentId,
                    pin.Version.ToString(),
                    release is null ? "unlisted" : release.Yanked ? "yanked" : "listed",
                    release?.Yanked == true ? release.YankedReason : null));
            }

            return From(pack, status, installed, releases) with
            {
                Mods = members,
                Vehicles = pack.Vehicles.Select(MemberView.Pin).ToArray(),
                Saves = pack.Saves.Select(MemberView.Pin).ToArray(),
            };
        }

        public static VersionView Unknown(DiagnosticView diagnostic) => new(
            "unknown",
            diagnostic.SpecVersion,
            diagnostic.Version!,
            false,
            null,
            null,
            ContentOutput.Name(GameCompatibility.Unknown),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            diagnostic.Reason);
    }

    private sealed record MemberView(string Id, string Version, string? State, string? YankedReason)
    {
        public static MemberView Pin(ModPackEntry pin) => new(pin.ContentId, pin.Version.ToString(), null, null);
    }

    private sealed record InstallView(
        string PackId,
        string Version,
        Guid InstanceId,
        string InstanceName,
        bool Complete,
        IReadOnlyList<MemberResultView> Members,
        IReadOnlyList<MessageView> Warnings,
        IReadOnlyList<MessageView> UnresolvedChoices,
        IReadOnlyList<MessageView> Conflicts,
        IReadOnlyList<SkippedView> Skipped,
        IReadOnlyList<DiagnosticView> Diagnostics)
    {
        public static InstallView From(
            ModPackMetadata pack,
            string instanceName,
            IReadOnlyList<MessageView> selectionWarnings,
            ModPackInstallResult result,
            IReadOnlyList<DiagnosticView> diagnostics) => new(
            pack.ModPackId,
            pack.Version.ToString(),
            result.InstanceId,
            instanceName,
            result.IsComplete,
            result.Members.Select(MemberResultView.From).ToArray(),
            selectionWarnings.Concat(result.Warnings.Select(MessageView.From)).ToArray(),
            result.Plan?.UnresolvedChoices.Select(MessageView.From).ToArray() ?? [],
            result.Plan?.Conflicts.Select(MessageView.From).ToArray() ?? [],
            pack.Vehicles.Select(pin => SkippedView.From("vehicle", pin))
                .Concat(pack.Saves.Select(pin => SkippedView.From("save", pin)))
                .ToArray(),
            diagnostics);
    }

    private sealed record MemberResultView(string Id, string Version, string Reason, string Status, string? Message, string? Location)
    {
        public static MemberResultView From(ModPackMemberResult member) => new(
            member.ModId,
            member.Version.ToString(),
            Name(member.Reason),
            Name(member.Status),
            member.Message,
            member.Location);
    }

    private sealed record MessageView(string Id, string Code, string Message)
    {
        public static MessageView From(PlanningMessage message) => new(message.ModId, message.Code, message.Message);
    }

    private sealed record SkippedView(string Section, string Id, string Version)
    {
        public static SkippedView From(string section, ModPackEntry pin) => new(section, pin.ContentId, pin.Version.ToString());
    }
}
