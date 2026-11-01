using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Network.Planning;

public sealed class RepositoryInstallPlanner : IInstallPlanner
{
    private const int SearchLimit = 100_000;
    private readonly ModDependencyResolver _resolver;
    private readonly ReleaseChannel _channel;

    /// <param name="channel">The release channel of a request that names none.</param>
    public RepositoryInstallPlanner(ModDependencyResolver resolver, ReleaseChannel channel = ReleaseChannel.Stable)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "The release channel is not defined.");

        _channel = channel;
    }

    public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var channel = request.Channel ?? _channel;
        var roots = BuildRoots(request, out var initialConflicts);
        var domains = await BuildDomainsAsync(request, roots, channel, initialConflicts, cancellationToken).ConfigureAwait(false);
        // a cached index answers at once, so without this the search of a large plan runs on the thread of the caller, which can be the UI thread
        return await Task.Run(() => Plan(request, channel, roots, domains, initialConflicts, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private InstallPlan Plan(InstallPlanningRequest request, ReleaseChannel channel, Dictionary<string, RequestedMod> roots, Dictionary<string, IReadOnlyList<RequestedMod?>> domains, IReadOnlyList<PlanningMessage> initialConflicts, CancellationToken cancellationToken)
    {
        var chosen = new Dictionary<string, RequestedMod?>(ModIds.Comparer);
        var states = 0;
        foreach (var group in BuildGroups(request, domains))
        {
            var ids = group.Where(domains.ContainsKey).OrderBy(value => value, ModIds.Comparer).ToList();
            if (ids.Count == 0) continue;

            // the other groups stay out of these plans, because what they add is the same in every plan of this group
            var groupRoots = ids.Where(roots.ContainsKey).ToDictionary(value => value, value => roots[value], ModIds.Comparer);
            // only an optional or suggested entry reaches a group with no request and no installed mod, so no plan selects it, and every other plan of the group has more mods and no lower rank
            if (groupRoots.Count == 0 && !request.Instance.Mods.Any(mod => group.Contains(mod.ModId)))
            {
                foreach (var id in ids) chosen[id] = null;
                continue;
            }

            var assigned = new Dictionary<string, RequestedMod?>(ModIds.Comparer);
            SearchResult? best = null;
            var bestAssigned = assigned;
            var hasNoOwnProblem = false;

            void Search(int index, int age)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++states > SearchLimit) return;
                if (index == ids.Count)
                {
                    var result = Evaluate(request, channel, groupRoots, domains, assigned, initialConflicts);
                    if (best is not null && result.Score.CompareTo(best.Score) >= 0) return;
                    best = result;
                    bestAssigned = new Dictionary<string, RequestedMod?>(assigned, ModIds.Comparer);
                    hasNoOwnProblem = HasNoOwnProblem(result, group, initialConflicts);
                    return;
                }
                var id = ids[index];
                var candidates = domains[id];
                var countsAge = groupRoots.TryGetValue(id, out var root) && !root.Exact;
                for (var position = 0; position < candidates.Count; position++)
                {
                    var candidateAge = countsAge ? age + position : age;
                    // no later leaf can win once it is older than a best plan that has no conflict or missing mod of this group, because those two rank above age
                    if (hasNoOwnProblem && candidateAge > best!.Age) break;
                    assigned[id] = candidates[position];
                    Search(index + 1, candidateAge);
                }
                assigned.Remove(id);
            }

            Search(0, 0);
            if (states > SearchLimit)
            {
                var conflict = new PlanningMessage("planning", PlanningMessageKind.SearchLimit) { Limit = SearchLimit, Value = string.Join(", ", groupRoots.Count > 0 ? ids.Where(groupRoots.ContainsKey) : ids) };
                return new InstallPlan(request.Instance.InstanceId, InstallPlanningState.Capture(request.Instance), [], [], [], [], [conflict], []);
            }
            foreach (var pair in bestAssigned) chosen[pair.Key] = pair.Value;
        }

        var ordered = domains.Keys.OrderBy(value => value, ModIds.Comparer).ToDictionary(value => value, value => chosen[value], ModIds.Comparer);
        return ToPlan(request, Evaluate(request, channel, roots, domains, ordered, initialConflicts));
    }

    /// <summary>
    /// The mods that can change the score of each other, and the installed mods that speak about them. Every message of a plan belongs to one group,
    /// and every rank of the score counts what adds up over the groups, so the best plan of each group together is the best plan of all,
    /// and the sorted order inside a group keeps the first of equal plans. Optional and suggested mods do not link, because they change only the open choices.
    /// A mod that is linked only through a mod that is already decided stays in the group, because an any_of entry reads every requested mod that requires its alternative.
    /// </summary>
    private static List<HashSet<string>> BuildGroups(InstallPlanningRequest request, Dictionary<string, IReadOnlyList<RequestedMod?>> domains)
    {
        var parents = new Dictionary<string, string>(ModIds.Comparer);
        string Find(string id) => parents.TryGetValue(id, out var parent) ? parents[id] = Find(parent) : id;
        void Link(string id, ModVersionMetadata release)
        {
            foreach (var target in release.Dependencies.Where(value => value.Kind is ModDependencyKind.Required or ModDependencyKind.Recommends or ModDependencyKind.Conflict).SelectMany(DependencyTargets))
            {
                var first = Find(id);
                var second = Find(target);
                if (!ModIds.Equals(first, second)) parents[first] = second;
            }
        }

        foreach (var (id, candidates) in domains)
            foreach (var candidate in candidates.OfType<RequestedMod>()) Link(id, candidate.Release);
        foreach (var mod in request.Instance.Mods) Link(mod.ModId, mod.Metadata);

        return domains.Keys.Concat(request.Instance.Mods.Select(value => value.ModId)).Distinct(ModIds.Comparer).GroupBy(Find, ModIds.Comparer).Select(group => group.ToHashSet(ModIds.Comparer)).ToList();
    }

    // the initial conflicts and the messages of other groups are the same in every plan of this group, so only the rest can get better
    private static bool HasNoOwnProblem(SearchResult result, HashSet<string> group, IReadOnlyList<PlanningMessage> initialConflicts)
        => !result.Conflicts.Any(value => group.Contains(value.ModId) && !initialConflicts.Contains(value))
            && !result.Warnings.Any(value => group.Contains(value.ModId) && value.Kind is PlanningMessageKind.MissingForVehicle or PlanningMessageKind.MissingForSave);

    private static Dictionary<string, RequestedMod> BuildRoots(InstallPlanningRequest request, out List<PlanningMessage> conflicts)
    {
        var roots = new Dictionary<string, RequestedMod>(ModIds.Comparer);
        conflicts = [];
        foreach (var item in request.Requested.OrderBy(value => value.Release.ModId, ModIds.Comparer))
        {
            // a vehicle or save is a folder under Vehicles or saves, never a mod of the instance, so only its exact release counts
            if (IsContent(item.Release))
            {
                roots[item.Release.ModId] = item with { Exact = true };
                continue;
            }

            // an update asks for the version a mod has, so only a request for another version of a pinned mod is a conflict
            if (Pinned(request, item.Release.ModId) is { } pinned)
            {
                if (item.Release.Version != pinned.Version)
                    conflicts.Add(new PlanningMessage(pinned.ModId, PlanningMessageKind.Pinned) { Version = pinned.Version, OtherVersion = item.Release.Version });
                continue;
            }
            if (RecordedForeign(request, item.Release.ModId) is { } recorded && item.Release.Version != recorded.Version)
            {
                conflicts.Add(Message(recorded.ModId, PlanningMessageKind.ForeignOwned));
                continue;
            }
            if (request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, item.Release.ModId)))
                conflicts.Add(Message(item.Release.ModId, PlanningMessageKind.ForeignOwned));
            if (roots.TryGetValue(item.Release.ModId, out var previous) && previous.Exact && item.Exact && previous.Release.Version != item.Release.Version)
                conflicts.Add(new PlanningMessage(item.Release.ModId, PlanningMessageKind.ExactPinConflict) { Version = previous.Release.Version, OtherVersion = item.Release.Version });
            else if (!roots.TryGetValue(item.Release.ModId, out previous) || item.Exact || !previous.Exact)
                roots[item.Release.ModId] = item;
        }
        return roots;
    }

    /// <summary>
    /// The candidates of every reachable mod, newest first, and last the yanked releases and those whose download is gone and that are not installed,
    /// because an installed copy is never replaced for its gone download. An exact request, a pinned mod and a recorded foreign mod have one candidate each,
    /// and every other candidate is inside the channel or already installed.
    /// </summary>
    private static async Task<Dictionary<string, IReadOnlyList<RequestedMod?>>> BuildDomainsAsync(InstallPlanningRequest request, Dictionary<string, RequestedMod> roots, ReleaseChannel channel, List<PlanningMessage> conflicts, CancellationToken cancellationToken)
    {
        var releases = new Dictionary<string, List<ModVersionMetadata>>(ModIds.Comparer);
        var pending = new Queue<string>(roots.Keys.OrderBy(value => value, ModIds.Comparer));
        var seen = new HashSet<string>(ModIds.Comparer);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!seen.Add(id)) continue;
            var values = new List<ModVersionMetadata>();
            var outsideChannel = false;
            roots.TryGetValue(id, out var root);
            if (root is not null && IsContent(root.Release))
                values.Add(root.Release);
            else if (Pinned(request, id) is { } pinned)
                values.Add(pinned.Metadata);
            else if (root is { Exact: true })
                values.Add(root.Release);
            else if (RecordedForeign(request, id) is { } recorded)
                values.Add(recorded.Metadata);
            else if (!request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, id)))
            {
                if (request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, id)) is { } installed && !roots.ContainsKey(id)) values.Add(installed.Metadata);
                foreach (var version in (await request.Repository.GetAvailableVersionsAsync(id, cancellationToken).ConfigureAwait(false)).OrderByDescending(value => value))
                {
                    var release = await request.Repository.GetReleaseAsync(id, version, cancellationToken).ConfigureAwait(false);
                    if (release is not { IsOffered: true }) continue;
                    if (channel.Includes(release.ReleaseStatus) || IsInstalled(request, release)) values.Add(release);
                    else outsideChannel = true;
                }
            }
            if (roots.TryGetValue(id, out root) && values.All(value => value.Version != root.Release.Version))
            {
                if (channel.Includes(root.Release.ReleaseStatus) || IsInstalled(request, root.Release)) values.Add(root.Release);
                else outsideChannel = true;
            }
            if (root is { Exact: false } && values.Count == 0 && outsideChannel)
                conflicts.Add(new PlanningMessage(id, PlanningMessageKind.OutsideChannel) { Channel = channel });
            releases[id] = values.DistinctBy(value => value.Version).OrderBy(value => value.Yanked || (value.Download.UnavailableSince is not null && !IsInstalled(request, value))).ThenByDescending(value => value.Version).ToList();
            foreach (var dependencyId in releases[id].SelectMany(DependencyIds).Distinct(ModIds.Comparer).OrderBy(value => value, ModIds.Comparer)) pending.Enqueue(dependencyId);
        }

        return releases.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<RequestedMod?>)pair.Value.Select(value => new RequestedMod(value, roots.TryGetValue(pair.Key, out var root) ? root.Reason : InstallReason.Dependency, roots.TryGetValue(pair.Key, out root) && root.Exact)).Cast<RequestedMod?>().Concat(roots.TryGetValue(pair.Key, out var requested) && requested.Exact ? [] : [null]).ToList(),
            ModIds.Comparer);
    }

    private static IEnumerable<string> DependencyIds(ModVersionMetadata release) => release.Dependencies.SelectMany(DependencyTargets);
    private static IEnumerable<string> DependencyTargets(ModDependency value) => value.IsAnyOf ? value.AnyOf.Select(item => item.ModId) : value.ModId is null ? [] : [value.ModId];

    private SearchResult Evaluate(InstallPlanningRequest request, ReleaseChannel channel, Dictionary<string, RequestedMod> roots, Dictionary<string, IReadOnlyList<RequestedMod?>> domains, Dictionary<string, RequestedMod?> assigned, IReadOnlyList<PlanningMessage> initialConflicts)
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
            // outside-channel already reported this request
            if (!selected.TryGetValue(root.Release.ModId, out var chosen)) { if (!initialConflicts.Any(value => value.Kind == PlanningMessageKind.OutsideChannel && ModIds.Equals(value.ModId, root.Release.ModId))) conflicts.Add(Message(root.Release.ModId, PlanningMessageKind.MissingRequest)); }
            else if (root.Exact && chosen.Release.Version != root.Release.Version) conflicts.Add(new PlanningMessage(root.Release.ModId, PlanningMessageKind.ExactPin) { Version = root.Release.Version });
        }

        foreach (var item in selected.Values.OrderBy(value => value.Release.ModId, ModIds.Comparer))
        {
            EvaluateRelease(request, channel, item.Release, selected, warnings, unresolved, conflicts, choices);
            foreach (var evaluation in _resolver.Evaluate(proposed, item.Release).Where(value => value.Outcome == DependencyOutcome.Conflict)) conflicts.Add(Message(item.Release.ModId, PlanningMessageKind.ProposedConflict, evaluation.Dependency));
        }
        EvaluateRetainedInstalled(request, selected, unresolved, conflicts);
        var age = roots.Values.Where(root => !root.Exact).Sum(root => CandidateIndex(domains[root.Release.ModId], assigned.GetValueOrDefault(root.Release.ModId)));
        return new SearchResult(selected, age, Sort(warnings), Sort(unresolved), Sort(conflicts), choices.OrderBy(value => value.Key, StringComparer.Ordinal).ToList());
    }

    private static int CandidateIndex(IReadOnlyList<RequestedMod?> candidates, RequestedMod? chosen)
    {
        for (var index = 0; index < candidates.Count; index++)
            if (Equals(candidates[index], chosen)) return index;
        return candidates.Count;
    }

    private static void EvaluateRelease(InstallPlanningRequest request, ReleaseChannel channel, ModVersionMetadata release, Dictionary<string, RequestedMod> selected, List<PlanningMessage> warnings, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts, List<PlanningChoice> choices)
    {
        if (release.Yanked) warnings.Add(new PlanningMessage(release.ModId, PlanningMessageKind.Yanked) { Value = release.YankedReason });
        if (release.Download.UnavailableSince is { } since && !IsInstalled(request, release)) warnings.Add(new PlanningMessage(release.ModId, PlanningMessageKind.Unavailable) { Version = release.Version, Since = since });
        // only an exact request reaches this
        if (!channel.Includes(release.ReleaseStatus) && !IsInstalled(request, release)) warnings.Add(new PlanningMessage(release.ModId, PlanningMessageKind.ReleaseChannel) { Version = release.Version, Status = release.ReleaseStatus, Channel = channel });
        var compatibility = Compatibility.Evaluate(release, request.GameVersion);
        // a stated game_max blocks an install (RFC 0091), but a copy the instance already holds only warns unless it is asked for by its exact version, so it does not block every other change
        var keeps = IsInstalled(request, release) && !(selected.TryGetValue(release.ModId, out var exactRequest) && exactRequest.Exact);
        if (Compatibility.IsAboveUpperBound(release, request.GameVersion)) (keeps ? warnings : conflicts).Add(new PlanningMessage(release.ModId, PlanningMessageKind.AboveGameMax) { Value = release.GameMax });
        else if (compatibility == GameCompatibility.Incompatible) conflicts.Add(new PlanningMessage(release.ModId, PlanningMessageKind.Incompatible) { Value = release.GameMin });
        else if (compatibility != GameCompatibility.Compatible) warnings.Add(new PlanningMessage(release.ModId, PlanningMessageKind.Compatibility) { Compatibility = compatibility });
        if (request.TargetPlatform is { } target)
        {
            var support = Compatibility.EvaluateOs(release.Os, target);
            if (!support.IsSupported) warnings.Add(new PlanningMessage(release.ModId, PlanningMessageKind.Platform) { Platform = target });
            foreach (var value in support.Unrecognized) warnings.Add(new PlanningMessage(release.ModId, PlanningMessageKind.PlatformUnknown) { Value = value });
        }

        for (var index = 0; index < release.Dependencies.Count; index++)
        {
            var dependency = release.Dependencies[index];
            var key = ChoiceKey(release.ModId, index, "recommendation");
            if (dependency.Kind == ModDependencyKind.Unknown) { unresolved.Add(Message(release.ModId, PlanningMessageKind.UnknownDependency, dependency)); continue; }
            if (dependency.Kind == ModDependencyKind.Recommends)
            {
                var chosen = request.Recommended?.Contains(key) ?? false;
                if (!chosen && WasOffered(request, release, dependency)) continue;
                if (chosen || !IsSatisfied(request, selected, dependency))
                    choices.Add(new PlanningChoice(key, release.ModId, PlanningChoiceKind.Recommendation, ["include", "exclude"], chosen ? "include" : "exclude", dependency));
                if (!chosen) continue;
            }
            if (dependency.Kind == ModDependencyKind.Suggests && !WasOffered(request, release, dependency) && !IsSatisfied(request, selected, dependency))
                choices.Add(new PlanningChoice(ChoiceKey(release.ModId, index, "suggestion"), release.ModId, PlanningChoiceKind.Suggestion, [dependency.ModId!], null, dependency));
            if (dependency.Kind is ModDependencyKind.Optional or ModDependencyKind.Suggests) continue;
            var missing = IsContent(release) && dependency.Kind == ModDependencyKind.Required;
            var dependencyUnresolved = missing ? new List<PlanningMessage>() : unresolved;
            var dependencyConflicts = missing ? new List<PlanningMessage>() : conflicts;
            if (dependency.IsAnyOf) EvaluateAlternative(request, release.ModId, ChoiceKey(release.ModId, index, "alternative"), dependency, selected, dependencyUnresolved, dependencyConflicts, choices);
            else EvaluateSingle(request, release.ModId, dependency, selected, dependencyUnresolved, dependencyConflicts);
            if (missing) AddMissing(release, dependency, dependencyUnresolved, dependencyConflicts, warnings, unresolved, conflicts);
        }
    }

    // RFC 0085: a required mod of a vehicle or save that cannot be installed does not stop the plan, because the player may go on without it
    private static void AddMissing(ModVersionMetadata release, ModDependency dependency, List<PlanningMessage> dependencyUnresolved, List<PlanningMessage> dependencyConflicts, List<PlanningMessage> warnings, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts)
    {
        foreach (var (messages, target) in new[] { (dependencyUnresolved, unresolved), (dependencyConflicts, conflicts) })
            foreach (var message in messages)
            {
                if (message.Kind is PlanningMessageKind.UnsatisfiedDependency or PlanningMessageKind.PinnedDependency or PlanningMessageKind.ForeignOwned or PlanningMessageKind.ForeignVersionUnknown or PlanningMessageKind.UnsatisfiedAlternative or PlanningMessageKind.ForeignAlternativeUnknown)
                    warnings.Add(new PlanningMessage(release.ModId, release.Type == ContentType.Save ? PlanningMessageKind.MissingForSave : PlanningMessageKind.MissingForVehicle) { Dependency = dependency, Value = message.Code });
                else
                    target.Add(message);
            }
    }

    private static void EvaluateSingle(InstallPlanningRequest request, string owner, ModDependency dependency, Dictionary<string, RequestedMod> selected, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts)
    {
        var version = FindManagedVersion(request, selected, dependency.ModId!);
        var foreign = request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, dependency.ModId));
        if (dependency.Kind == ModDependencyKind.Conflict)
        {
            if (version is { } found && dependency.BoundsContain(found)) conflicts.Add(Message(owner, PlanningMessageKind.DependencyConflict, dependency));
            else if (foreign && (dependency.MinVersion is not null || dependency.MaxVersion is not null)) unresolved.Add(Message(owner, PlanningMessageKind.ForeignConflictUnknown, dependency));
            else if (foreign) conflicts.Add(Message(owner, PlanningMessageKind.ForeignConflict, dependency));
            return;
        }
        if (version is { } installed && dependency.BoundsContain(installed)) return;
        if (foreign && dependency.MinVersion is null && dependency.MaxVersion is null) return;
        if (foreign) unresolved.Add(Message(owner, PlanningMessageKind.ForeignVersionUnknown, dependency));
        else if (Pinned(request, dependency.ModId!) is { } pinned) conflicts.Add(new PlanningMessage(owner, PlanningMessageKind.PinnedDependency) { Dependency = dependency, Value = pinned.ModId, Version = pinned.Version });
        else if (RecordedForeign(request, dependency.ModId!) is { } recorded) conflicts.Add(Message(recorded.ModId, PlanningMessageKind.ForeignOwned, dependency));
        else conflicts.Add(Message(owner, PlanningMessageKind.UnsatisfiedDependency, dependency));
    }

    private static void EvaluateAlternative(InstallPlanningRequest request, string owner, string key, ModDependency dependency, Dictionary<string, RequestedMod> selected, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts, List<PlanningChoice> choices)
    {
        var satisfied = dependency.AnyOf!.FirstOrDefault(value => ExistingAlternativeSatisfied(request, selected, value) && ForcedAlternative(request, selected, value.ModId));
        string? requested = null;
        request.Alternatives?.TryGetValue(key, out requested);
        choices.Add(new PlanningChoice(key, owner, PlanningChoiceKind.Alternative, dependency.AnyOf!.Select(value => value.ModId).ToList(), requested ?? satisfied?.ModId, dependency));
        if (satisfied is not null && requested is null) return;
        if (requested is null) { unresolved.Add(Message(owner, PlanningMessageKind.AlternativeChoice, dependency)); return; }
        var chosen = dependency.AnyOf!.FirstOrDefault(value => ModIds.Equals(value.ModId, requested));
        if (chosen is null) { conflicts.Add(new PlanningMessage(owner, PlanningMessageKind.InvalidAlternative) { Dependency = dependency, Value = requested }); return; }
        if (AlternativeSatisfied(request, selected, chosen)) return;
        if (request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, chosen.ModId))) unresolved.Add(new PlanningMessage(owner, PlanningMessageKind.ForeignAlternativeUnknown) { Dependency = dependency, Value = chosen.ModId });
        else if (Pinned(request, chosen.ModId) is { } pinned) conflicts.Add(new PlanningMessage(owner, PlanningMessageKind.PinnedDependency) { Dependency = new ModDependency(chosen.ModId, ModDependencyKind.Required, chosen.MinVersion, chosen.MaxVersion), Value = pinned.ModId, Version = pinned.Version });
        else if (RecordedForeign(request, chosen.ModId) is { } recorded) conflicts.Add(Message(recorded.ModId, PlanningMessageKind.ForeignOwned, dependency));
        else conflicts.Add(new PlanningMessage(owner, PlanningMessageKind.UnsatisfiedAlternative) { Dependency = dependency, Value = chosen.ModId });
    }

    private static void EvaluateRetainedInstalled(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, List<PlanningMessage> unresolved, List<PlanningMessage> conflicts)
    {
        foreach (var installed in request.Instance.Mods.Where(value => !selected.ContainsKey(value.ModId)))
            foreach (var dependency in installed.Metadata.Dependencies)
            {
                if (dependency.Kind == ModDependencyKind.Required && dependency.IsAnyOf && !dependency.AnyOf.Any(value => AlternativeSatisfied(request, selected, value)))
                {
                    if (dependency.AnyOf.Any(value => request.Instance.ForeignMods.Any(foreign => ModIds.Equals(foreign.ModId, value.ModId)) && (value.MinVersion is not null || value.MaxVersion is not null))) unresolved.Add(Message(installed.ModId, PlanningMessageKind.ForeignAlternativeUnknown, dependency));
                    else conflicts.Add(Message(installed.ModId, PlanningMessageKind.RetainedAlternative, dependency));
                }
                else if (!dependency.IsAnyOf && selected.TryGetValue(dependency.ModId!, out var planned))
                {
                    if (dependency.Kind == ModDependencyKind.Required && !dependency.BoundsContain(planned.Release.Version)) conflicts.Add(Message(installed.ModId, PlanningMessageKind.RetainedDependent, dependency));
                    if (dependency.Kind == ModDependencyKind.Conflict && dependency.BoundsContain(planned.Release.Version)) conflicts.Add(Message(installed.ModId, PlanningMessageKind.RetainedConflict, dependency));
                }
                else if (!dependency.IsAnyOf && dependency.Kind == ModDependencyKind.Required)
                {
                    var local = request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, dependency.ModId));
                    if (local is not null && dependency.BoundsContain(local.Version)) continue;
                    var foreign = request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, dependency.ModId));
                    if (foreign && (dependency.MinVersion is not null || dependency.MaxVersion is not null)) unresolved.Add(Message(installed.ModId, PlanningMessageKind.ForeignVersionUnknown, dependency));
                    else if (!foreign) conflicts.Add(Message(installed.ModId, PlanningMessageKind.RetainedUnsatisfied, dependency));
                }
            }
    }

    private static ModVersion? FindManagedVersion(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, string id) => selected.TryGetValue(id, out var item) ? item.Release.Version : request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, id))?.Version;
    private static bool AlternativeSatisfied(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, ModDependencyAlternative alternative) => FindManagedVersion(request, selected, alternative.ModId) is { } version ? alternative.BoundsContain(version) : request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, alternative.ModId)) && alternative.MinVersion is null && alternative.MaxVersion is null;
    private static bool ExistingAlternativeSatisfied(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, ModDependencyAlternative alternative) => FindManagedVersion(request, selected, alternative.ModId) is { } version ? alternative.BoundsContain(version) : request.Instance.ForeignMods.Any(value => ModIds.Equals(value.ModId, alternative.ModId)) && alternative.MinVersion is null && alternative.MaxVersion is null;
    private static bool IsSatisfied(InstallPlanningRequest request, Dictionary<string, RequestedMod> selected, ModDependency dependency) => dependency.IsAnyOf ? dependency.AnyOf.Any(value => AlternativeSatisfied(request, selected, value)) : AlternativeSatisfied(request, selected, new ModDependencyAlternative(dependency.ModId, dependency.MinVersion, dependency.MaxVersion));

    // the installed release already offered this entry, so the user decided on it when that release was installed
    private static bool WasOffered(InstallPlanningRequest request, ModVersionMetadata release, ModDependency dependency)
    {
        var installed = request.Instance.Mods.FirstOrDefault(value => ModIds.Equals(value.ModId, release.ModId));
        if (installed is null) return false;
        if (installed.Version == release.Version) return true;
        var targets = TargetIds(dependency);
        return installed.Metadata.Dependencies.Any(value => value.Kind == dependency.Kind && TargetIds(value).SetEquals(targets));
    }

    private static HashSet<string> TargetIds(ModDependency dependency) => (dependency.IsAnyOf ? dependency.AnyOf.Select(value => value.ModId) : [dependency.ModId!]).ToHashSet(ModIds.Comparer);

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
        var planned = selected.Values.Where(value => !IsContent(value.Release)).Select(value => new InstalledMod(value.Release.ModId, value.Release.Version, value.Reason, DateTimeOffset.UnixEpoch, value.Release));
        var foreign = request.Instance.ForeignMods.Where(value => !selected.ContainsKey(value.ModId)).ToList();
        return Borea.Core.Instances.Instance.FromExisting(request.Instance.InstanceId, request.Instance.Name, request.Instance.Source, request.Instance.CreatedAt, retained.Concat(planned).ToList(), foreign, request.Instance.IsFavorite);
    }

    private static InstallPlan ToPlan(InstallPlanningRequest request, SearchResult result)
    {
        var ordered = OrderOperations(request, result.Selected).Where(value => !IsContent(value.Release)).ToList();
        var selections = result.Selected.Values.Where(value => !IsContent(value.Release)).OrderBy(value => value.Release.ModId, ModIds.Comparer).Select(value => new PlannedSelection(value.Release, value.Reason, IsInstalled(request, value.Release))).ToList();
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
    private static bool IsContent(ModVersionMetadata release) => release.Type.IsVehicleOrSave();
    private static InstalledMod? Pinned(InstallPlanningRequest request, string id) => request.Instance.Mods.FirstOrDefault(value => value.IsPinned && ModIds.Equals(value.ModId, id));
    private static bool IsInstalled(InstallPlanningRequest request, ModVersionMetadata release) => request.Instance.Mods.Any(value => ModIds.Equals(value.ModId, release.ModId) && value.Version == release.Version);
    // a mod that the player installed keeps its version, because Borea replaces only a folder that it owns
    private static InstalledMod? RecordedForeign(InstallPlanningRequest request, string id) => request.Instance.Mods.FirstOrDefault(value => value.Ownership == ModInstallOwnership.Foreign && ModIds.Equals(value.ModId, id));
    private static string ChoiceKey(string owner, int index, string kind) => $"{owner}:dependency:{index}:{kind}";
    private static PlanningMessage Message(string id, PlanningMessageKind kind, ModDependency? dependency = null) => new(id, kind) { Dependency = dependency };
    private static IReadOnlyList<PlanningMessage> Sort(List<PlanningMessage> values) => values.DistinctBy(value => (value.ModId, value.Code, value.Message)).OrderBy(value => value.ModId, ModIds.Comparer).ThenBy(value => value.Code, StringComparer.Ordinal).ThenBy(value => value.Message, StringComparer.Ordinal).ToList();

    /// <param name="Age">How far the requests that are not exact sit behind their newest candidate.</param>
    private sealed record SearchResult(Dictionary<string, RequestedMod> Selected, int Age, IReadOnlyList<PlanningMessage> Warnings, IReadOnlyList<PlanningMessage> Unresolved, IReadOnlyList<PlanningMessage> Conflicts, IReadOnlyList<PlanningChoice> Choices)
    {
        public SearchScore Score => new(Conflicts.Count, Conflicts.Count(value => value.Kind is PlanningMessageKind.UnsatisfiedDependency or PlanningMessageKind.PinnedDependency or PlanningMessageKind.MissingRequest or PlanningMessageKind.RetainedUnsatisfied) + Warnings.Count(value => value.Kind is PlanningMessageKind.MissingForVehicle or PlanningMessageKind.MissingForSave), Age, Unresolved.Count, Warnings.Count, Selected.Count);
    }
    // age ranks above open choices, warnings and the number of mods, so a request does not fall back to an older release to avoid the dependencies of the newest one
    private readonly record struct SearchScore(int Conflicts, int Missing, int Age, int Unresolved, int Warnings, int Selected) : IComparable<SearchScore>
    {
        public int CompareTo(SearchScore other) { var value = Conflicts.CompareTo(other.Conflicts); if (value != 0) return value; value = Missing.CompareTo(other.Missing); if (value != 0) return value; value = Age.CompareTo(other.Age); if (value != 0) return value; value = Unresolved.CompareTo(other.Unresolved); if (value != 0) return value; value = Warnings.CompareTo(other.Warnings); if (value != 0) return value; return Selected.CompareTo(other.Selected); }
    }
}
