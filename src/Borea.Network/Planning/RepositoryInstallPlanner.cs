using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Network.Planning;

public sealed class RepositoryInstallPlanner : IInstallPlanner
{
    private const int SearchLimit = 100_000;
    private readonly ModDependencyResolver _resolver;

    public RepositoryInstallPlanner(ModDependencyResolver resolver) => _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var roots = BuildRoots(request, out var initialConflicts);
        var domains = await BuildDomainsAsync(request, roots, cancellationToken).ConfigureAwait(false);
        var ids = domains.Keys.OrderBy(value => value, ModIds.Comparer).ToList();
        SearchResult? best = null;
        var states = 0;
        var truncated = false;
        var assigned = new Dictionary<string, RequestedMod?>(ModIds.Comparer);

        void Search(int index)
        {
            if (++states > SearchLimit) { truncated = true; return; }
            if (index == ids.Count)
            {
                var result = Evaluate(request, roots, assigned, initialConflicts);
                if (best is null || result.Score.CompareTo(best.Score) < 0) best = result;
                return;
            }
            var id = ids[index];
            foreach (var candidate in domains[id])
            {
                assigned[id] = candidate;
                Search(index + 1);
            }
            assigned.Remove(id);
        }

        Search(0);
        if (truncated)
        {
            var conflict = Message("planning", "search-limit", $"Planning exceeded the deterministic limit of {SearchLimit} candidate states.");
            return new InstallPlan(request.Instance.InstanceId, InstallPlanningState.Capture(request.Instance), [], [], [], [], [conflict], []);
        }
        if (best is null)
        {
            var conflict = Message("planning", "search-limit", $"Planning exceeded the deterministic limit of {SearchLimit} candidate states.");
            return new InstallPlan(request.Instance.InstanceId, InstallPlanningState.Capture(request.Instance), [], [], [], [], [conflict], []);
        }
        return ToPlan(request, best);
    }

    private static Dictionary<string, RequestedMod> BuildRoots(InstallPlanningRequest request, out List<PlanningMessage> conflicts)
    {
        var roots = new Dictionary<string, RequestedMod>(ModIds.Comparer);
        conflicts = [];
        foreach (var item in request.Requested.OrderBy(value => value.Release.ModId, ModIds.Comparer))
        {
            if (request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, item.Release.ModId)))
                conflicts.Add(Message(item.Release.ModId, "foreign-owned", "A managed install cannot replace foreign content."));
            if (roots.TryGetValue(item.Release.ModId, out var previous) && previous.Exact && item.Exact && previous.Release.Version != item.Release.Version)
                conflicts.Add(Message(item.Release.ModId, "exact-pin-conflict", $"Exact versions {previous.Release.Version} and {item.Release.Version} were both requested."));
            else if (!roots.TryGetValue(item.Release.ModId, out previous) || item.Exact || !previous.Exact)
                roots[item.Release.ModId] = item;
        }
        return roots;
    }

    private static async Task<Dictionary<string, IReadOnlyList<RequestedMod?>>> BuildDomainsAsync(InstallPlanningRequest request, Dictionary<string, RequestedMod> roots, CancellationToken cancellationToken)
    {
        var releases = new Dictionary<string, List<ModVersionMetadata>>(ModIds.Comparer);
        var pending = new Queue<string>(roots.Keys.OrderBy(value => value, ModIds.Comparer));
        var seen = new HashSet<string>(ModIds.Comparer);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!seen.Add(id)) continue;
            var values = new List<ModVersionMetadata>();
            if (roots.TryGetValue(id, out var root) && root.Exact)
                values.Add(root.Release);
            else if (!request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, id)))
            {
                if (request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, id)) is { } installed && !roots.ContainsKey(id)) values.Add(installed.Metadata);
                foreach (var version in (await request.Repository.GetAvailableVersionsAsync(id, cancellationToken).ConfigureAwait(false)).OrderByDescending(value => value))
                {
                    var release = await request.Repository.GetReleaseAsync(id, version, cancellationToken).ConfigureAwait(false);
                    if (release is { Yanked: false }) values.Add(release);
                }
            }
            if (roots.TryGetValue(id, out root) && values.All(value => value.Version != root.Release.Version)) values.Add(root.Release);
            releases[id] = values.DistinctBy(value => value.Version).OrderByDescending(value => value.Version).ToList();
            foreach (var dependencyId in releases[id].SelectMany(DependencyIds).Distinct(ModIds.Comparer).OrderBy(value => value, ModIds.Comparer)) pending.Enqueue(dependencyId);
        }

        return releases.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<RequestedMod?>)pair.Value.Select(value => new RequestedMod(value, roots.TryGetValue(pair.Key, out var root) ? root.Reason : InstallReason.Dependency, roots.TryGetValue(pair.Key, out root) && root.Exact)).Cast<RequestedMod?>().Concat(roots.TryGetValue(pair.Key, out var requested) && requested.Exact ? [] : [null]).ToList(),
            ModIds.Comparer);
    }

    private static IEnumerable<string> DependencyIds(ModVersionMetadata release) => release.Dependencies.SelectMany(value => value.IsAnyOf ? value.AnyOf.Select(item => item.ModId) : value.ModId is null ? [] : [value.ModId]);

    private SearchResult Evaluate(InstallPlanningRequest request, Dictionary<string, RequestedMod> roots, Dictionary<string, RequestedMod?> assigned, IReadOnlyList<PlanningMessage> initialConflicts)
    {
        var selected = assigned.Where(value => value.Value is not null).ToDictionary(value => value.Key, value => value.Value!, ModIds.Comparer);
        ExpandInstalledClosure(request, roots, selected);
        var warnings = new List<PlanningMessage>();
        var unresolved = new List<PlanningMessage>();
        var conflicts = new List<PlanningMessage>(initialConflicts);
        var choices = new List<PlanningChoice>();
        var proposed = BuildProposedInstance(request, selected);

        foreach (var root in roots.Values)
        {
            if (!selected.TryGetValue(root.Release.ModId, out var chosen)) conflicts.Add(Message(root.Release.ModId, "missing-request", "No release was selected for the request."));
            else if (root.Exact && chosen.Release.Version != root.Release.Version) conflicts.Add(Message(root.Release.ModId, "exact-pin", $"Exact version {root.Release.Version} was not selected."));
        }

        foreach (var item in selected.Values.OrderBy(value => value.Release.ModId, ModIds.Comparer))
        {
            EvaluateRelease(request, item.Release, selected, warnings, unresolved, conflicts, choices);
            foreach (var evaluation in _resolver.Evaluate(proposed, item.Release).Where(value => value.Outcome == DependencyOutcome.Conflict)) conflicts.Add(Message(item.Release.ModId, "proposed-conflict", evaluation.Dependency.ToString()));
        }
        EvaluateRetainedInstalled(request, selected, unresolved, conflicts);
        return new SearchResult(selected, Sort(warnings), Sort(unresolved), Sort(conflicts), choices.OrderBy(value => value.Key, StringComparer.Ordinal).ToList());
    }

    private static void EvaluateRelease(InstallPlanningRequest request, ModVersionMetadata release, Dictionary<string, RequestedMod> selected, List<PlanningMessage> warnings, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts, List<PlanningChoice> choices)
    {
        if (release.Yanked) warnings.Add(Message(release.ModId, "yanked", release.YankedReason ?? "The selected release is yanked."));
        var compatibility = Compatibility.Evaluate(release, request.GameVersion);
        if (compatibility == GameCompatibility.Incompatible) conflicts.Add(Message(release.ModId, "incompatible", "The release is incompatible with the target game."));
        else if (compatibility != GameCompatibility.Compatible) warnings.Add(Message(release.ModId, "compatibility", $"Game compatibility is {compatibility}."));
        if (request.TargetPlatform is { } target)
        {
            var support = Compatibility.EvaluateOs(release.Os, target);
            if (!support.IsSupported) warnings.Add(Message(release.ModId, "platform", $"The release does not list {target} as a supported platform."));
            foreach (var value in support.Unrecognized) warnings.Add(Message(release.ModId, "platform-unknown", $"The release contains the unknown platform '{value}'."));
        }

        for (var index = 0; index < release.Dependencies.Count; index++)
        {
            var dependency = release.Dependencies[index];
            var key = ChoiceKey(release.ModId, index, "recommendation");
            if (dependency.Kind == ModDependencyKind.Unknown) { unresolved.Add(Message(release.ModId, "unknown-dependency", dependency.ToString())); continue; }
            if (dependency.Kind == ModDependencyKind.Recommends)
            {
                var chosen = request.Recommended?.Contains(key) ?? false;
                choices.Add(new PlanningChoice(key, release.ModId, "recommendation", ["include", "exclude"], chosen ? "include" : "exclude"));
                if (!chosen) continue;
            }
            if (dependency.Kind is ModDependencyKind.Optional or ModDependencyKind.Suggests) continue;
            if (dependency.IsAnyOf) EvaluateAlternative(request, release.ModId, ChoiceKey(release.ModId, index, "alternative"), dependency, selected, unresolved, conflicts, choices);
            else EvaluateSingle(request, release.ModId, dependency, selected, unresolved, conflicts);
        }
    }

    private static void EvaluateSingle(InstallPlanningRequest request, string owner, ModDependency dependency, Dictionary<string, RequestedMod> selected, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts)
    {
        var version = FindManagedVersion(request, selected, dependency.ModId!);
        var foreign = request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, dependency.ModId));
        if (dependency.Kind == ModDependencyKind.Conflict)
        {
            if (version is { } found && dependency.BoundsContain(found)) conflicts.Add(Message(owner, "dependency-conflict", dependency.ToString()));
            else if (foreign && (dependency.MinVersion is not null || dependency.MaxVersion is not null)) unresolved.Add(Message(owner, "foreign-conflict-unknown", dependency.ToString()));
            else if (foreign) conflicts.Add(Message(owner, "foreign-conflict", dependency.ToString()));
            return;
        }
        if (version is { } installed && dependency.BoundsContain(installed)) return;
        if (foreign && dependency.MinVersion is null && dependency.MaxVersion is null) return;
        if (foreign) unresolved.Add(Message(owner, "foreign-version-unknown", dependency.ToString()));
        else conflicts.Add(Message(owner, "unsatisfied-dependency", dependency.ToString()));
    }

    private static void EvaluateAlternative(InstallPlanningRequest request, string owner, string key, ModDependency dependency, Dictionary<string, RequestedMod> selected, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts, List<PlanningChoice> choices)
    {
        var satisfied = dependency.AnyOf!.FirstOrDefault(value => ExistingAlternativeSatisfied(request, selected, value) && ForcedAlternative(request, selected, value.ModId));
        string? requested = null;
        request.Alternatives?.TryGetValue(key, out requested);
        choices.Add(new PlanningChoice(key, owner, "alternative", dependency.AnyOf!.Select(value => value.ModId).ToList(), requested ?? satisfied?.ModId));
        if (satisfied is not null && requested is null) return;
        if (requested is null) { unresolved.Add(Message(owner, "alternative-choice", $"Select one alternative for {dependency}.")); return; }
        var chosen = dependency.AnyOf!.FirstOrDefault(value => ModIds.Equals(value.ModId, requested));
        if (chosen is null) { conflicts.Add(Message(owner, "invalid-alternative", $"'{requested}' is not an available alternative.")); return; }
        if (AlternativeSatisfied(request, selected, chosen)) return;
        if (request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, chosen.ModId))) unresolved.Add(Message(owner, "foreign-alternative-unknown", chosen.ModId));
        else conflicts.Add(Message(owner, "unsatisfied-alternative", chosen.ModId));
    }

    private static void EvaluateRetainedInstalled(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts)
    {
        foreach (var installed in request.Instance.Mods.Where(value => !selected.ContainsKey(value.ModId)))
            foreach (var dependency in installed.Metadata.Dependencies)
            {
                if (dependency.Kind == ModDependencyKind.Required && dependency.IsAnyOf && !dependency.AnyOf.Any(value => AlternativeSatisfied(request, selected, value)))
                {
                    if (dependency.AnyOf.Any(value => request.Instance.ForeignMods.Any(foreign => ModIds.Equals(foreign.ModId, value.ModId)) && (value.MinVersion is not null || value.MaxVersion is not null))) unresolved.Add(Message(installed.ModId, "foreign-alternative-unknown", dependency.ToString()));
                    else conflicts.Add(Message(installed.ModId, "retained-alternative", dependency.ToString()));
                }
                else if (!dependency.IsAnyOf && selected.TryGetValue(dependency.ModId!, out var planned))
                {
                    if (dependency.Kind == ModDependencyKind.Required && !dependency.BoundsContain(planned.Release.Version)) conflicts.Add(Message(installed.ModId, "retained-dependent", dependency.ToString()));
                    if (dependency.Kind == ModDependencyKind.Conflict && dependency.BoundsContain(planned.Release.Version)) conflicts.Add(Message(installed.ModId, "retained-conflict", dependency.ToString()));
                }
                else if (!dependency.IsAnyOf && dependency.Kind == ModDependencyKind.Required)
                {
                    var local = request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, dependency.ModId));
                    if (local is not null && dependency.BoundsContain(local.Version)) continue;
                    var foreign = request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, dependency.ModId));
                    if (foreign && (dependency.MinVersion is not null || dependency.MaxVersion is not null)) unresolved.Add(Message(installed.ModId, "foreign-version-unknown", dependency.ToString()));
                    else if (!foreign) conflicts.Add(Message(installed.ModId, "retained-unsatisfied", dependency.ToString()));
                }
            }
    }

    private static ModVersion? FindManagedVersion(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, string id) => selected.TryGetValue(id, out var item) ? item.Release.Version : request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, id))?.Version;
    private static bool AlternativeSatisfied(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, ModDependencyAlternative alternative) => FindManagedVersion(request, selected, alternative.ModId) is { } version ? alternative.BoundsContain(version) : request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, alternative.ModId)) && alternative.MinVersion is null && alternative.MaxVersion is null;
    private static bool ExistingAlternativeSatisfied(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, ModDependencyAlternative alternative) => FindManagedVersion(request, selected, alternative.ModId) is { } version ? alternative.BoundsContain(version) : request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, alternative.ModId)) && alternative.MinVersion is null && alternative.MaxVersion is null;

    private static bool ForcedAlternative(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, string alternativeId)
    {
        if (request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, alternativeId)) is { } installed && (!selected.TryGetValue(alternativeId, out var planned) || planned.Release.Version == installed.Version)) return true;
        var forced = new HashSet<string>(request.Requested.Select(value => value.Release.ModId), ModIds.Comparer);
        var pending = new Queue<string>(forced);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!selected.TryGetValue(id, out var item)) continue;
            foreach (var dependency in item.Release.Dependencies.Where(value => !value.IsAnyOf && value.Kind == ModDependencyKind.Required))
                if (forced.Add(dependency.ModId!)) pending.Enqueue(dependency.ModId!);
        }
        return forced.Contains(alternativeId);
    }

    private static void ExpandInstalledClosure(InstallPlanningRequest request, Dictionary<string, RequestedMod> roots, Dictionary<string, RequestedMod> selected)
    {
        var pending = new Queue<RequestedMod>(selected.Values);
        while (pending.Count > 0)
        {
            var item = pending.Dequeue();
            foreach (var dependency in item.Release.Dependencies.Where(value => value.Kind == ModDependencyKind.Required && !value.IsAnyOf))
            {
                if (selected.ContainsKey(dependency.ModId!)) continue;
                var installed = request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, dependency.ModId));
                if (installed is null || !dependency.BoundsContain(installed.Version)) continue;
                var reason = roots.TryGetValue(installed.ModId, out var root) ? root.Reason : InstallReason.Dependency;
                var added = new RequestedMod(installed.Metadata, reason, Exact: false);
                selected[installed.ModId] = added;
                pending.Enqueue(added);
            }
        }
    }

    private static Borea.Core.Instances.Instance BuildProposedInstance(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected)
    {
        var retained = request.Instance.Mods.Where(value => !selected.ContainsKey(value.ModId));
        var planned = selected.Values.Select(value => new InstalledMod(value.Release.ModId, value.Release.Version, value.Reason, DateTimeOffset.UnixEpoch, value.Release));
        var foreign = request.Instance.ForeignMods.Where(value => !selected.ContainsKey(value.ModId)).ToList();
        return Borea.Core.Instances.Instance.FromExisting(request.Instance.InstanceId, request.Instance.Name, request.Instance.Source, request.Instance.CreatedAt, retained.Concat(planned).ToList(), foreign, request.Instance.IsFavorite);
    }

    private static InstallPlan ToPlan(InstallPlanningRequest request, SearchResult result)
    {
        var ordered = OrderOperations(request, result.Selected);
        var selections = result.Selected.Values.OrderBy(value => value.Release.ModId, ModIds.Comparer).Select(value => new PlannedSelection(value.Release, value.Reason, IsInstalled(request, value.Release))).ToList();
        var operations = ordered.Where(value => !IsInstalled(request, value.Release)).Select(value => new PlannedInstall(value.Release, value.Reason, Compatibility.Evaluate(value.Release, request.GameVersion), request.TargetPlatform is { } target ? Compatibility.EvaluateOs(value.Release.Os, target) : null)).ToList();
        return new InstallPlan(request.Instance.InstanceId, InstallPlanningState.Capture(request.Instance), selections, operations, result.Warnings, result.Unresolved, result.Conflicts, result.Choices);
    }

    private static IReadOnlyList<RequestedMod> OrderOperations(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected)
    {
        var result = new List<RequestedMod>(); var visited = new HashSet<string>(ModIds.Comparer);
        void Visit(RequestedMod item)
        {
            if (!visited.Add(item.Release.ModId)) return;
            for (var index = 0; index < item.Release.Dependencies.Count; index++)
            {
                var dependency = item.Release.Dependencies[index]; string? id = dependency.ModId;
                if (dependency.IsAnyOf) { var key = ChoiceKey(item.Release.ModId, index, "alternative"); id = request.Alternatives is not null && request.Alternatives.TryGetValue(key, out var chosen) ? chosen : dependency.AnyOf.FirstOrDefault(value => AlternativeSatisfied(request, selected, value))?.ModId; }
                if (id is not null && selected.TryGetValue(id, out var child)) Visit(child);
            }
            result.Add(item);
        }
        foreach (var item in selected.Values.OrderBy(value => value.Release.ModId, ModIds.Comparer)) Visit(item);
        return result;
    }

    private static void Validate(InstallPlanningRequest request) { ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Instance); ArgumentNullException.ThrowIfNull(request.Requested); ArgumentNullException.ThrowIfNull(request.Repository); }
    private static bool IsInstalled(InstallPlanningRequest request, ModVersionMetadata release) => request.Instance.Mods.Any(value => ModIds.Equals(value.ModId, release.ModId) && value.Version == release.Version);
    private static string ChoiceKey(string owner, int index, string kind) => $"{owner}:dependency:{index}:{kind}";
    private static PlanningMessage Message(string id, string code, string message) => new(id, code, message);
    private static IReadOnlyList<PlanningMessage> Sort(List<PlanningMessage> values) => values.Distinct().OrderBy(value => value.ModId, ModIds.Comparer).ThenBy(value => value.Code, StringComparer.Ordinal).ThenBy(value => value.Message, StringComparer.Ordinal).ToList();

    private sealed record SearchResult(Dictionary<string, RequestedMod> Selected, IReadOnlyList<PlanningMessage> Warnings, IReadOnlyList<PlanningMessage> Unresolved, IReadOnlyList<PlanningMessage> Conflicts, IReadOnlyList<PlanningChoice> Choices)
    {
        public SearchScore Score => new(Conflicts.Count, Conflicts.Count(value => value.Code is "unsatisfied-dependency" or "missing-request" or "retained-unsatisfied"), Unresolved.Count, Warnings.Count, Selected.Count);
    }
    private readonly record struct SearchScore(int Conflicts, int Missing, int Unresolved, int Warnings, int Selected) : IComparable<SearchScore>
    {
        public int CompareTo(SearchScore other) { var value = Conflicts.CompareTo(other.Conflicts); if (value != 0) return value; value = Missing.CompareTo(other.Missing); if (value != 0) return value; value = Unresolved.CompareTo(other.Unresolved); if (value != 0) return value; value = Warnings.CompareTo(other.Warnings); if (value != 0) return value; return Selected.CompareTo(other.Selected); }
    }
}
