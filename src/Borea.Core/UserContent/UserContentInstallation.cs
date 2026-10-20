using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.State;

namespace Borea.Core.UserContent;

/// <summary>
/// Installs or updates one vehicle or save with its mods, for the CLI and the App alike (RFC 0085). The archive
/// is downloaded and checked first, so a broken archive changes nothing; then the mods install enabled, and the
/// folder moves into place last.
/// </summary>
public sealed class UserContentInstallation
{
    private readonly IInstallPlanner _planner;
    private readonly IInstallPlanExecutor _executor;
    private readonly IModStateRepository _modState;
    private readonly IUserContentInstaller _installer;

    public UserContentInstallation(IInstallPlanner planner, IInstallPlanExecutor executor, IModStateRepository modState, IUserContentInstaller installer)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    }

    public async Task<UserContentPlan> PlanInstallAsync(UserContentRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var check = await _installer.CheckInstallAsync(request.Instance.InstanceId, request.Release, request.Name, cancellationToken).ConfigureAwait(false);
        return await PlanAsync(request, installed: null, check, discardChanges: false, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="discardChanges">Whether files the player changed since the install may go into the backups.</param>
    public async Task<UserContentPlan> PlanUpdateAsync(InstalledContentRecord installed, UserContentRequest request, bool discardChanges, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        Validate(request);
        var check = await _installer.CheckUpdateAsync(request.Instance.InstanceId, installed, request.Release, request.Name, discardChanges, cancellationToken).ConfigureAwait(false);
        return await PlanAsync(request, installed, check, discardChanges, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a ready plan. A <see cref="UserContentOutcome"/> other than installed or updated means the folder
    /// step found something in its way before any mod was installed, and nothing changed.
    /// </summary>
    /// <param name="allowMissing">Whether the player goes on without the mods of <see cref="UserContentPlan.Missing"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// The plan is not ready, or it has missing mods without <paramref name="allowMissing"/>, or the folder step failed
    /// after the mods were installed, which stay installed and enabled.
    /// </exception>
    public async Task<UserContentResult> ExecuteAsync(UserContentPlan plan, bool allowMissing, IProgress<InstallProgress>? progress = null, InstallStop? stop = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Mods.IsReady)
            throw new InvalidOperationException("The install plan has unresolved choices or conflicts.");
        if (plan.Check.Outcome != UserContentOutcome.Ready)
            return plan.Check;
        if (plan.Missing.Count > 0 && !allowMissing)
            throw new InvalidOperationException($"'{plan.Release.ModId}' needs mods that Borea cannot install or enable. {string.Join(" ", plan.Missing.Select(message => message.Message))}");

        var instanceId = plan.Mods.InstanceId;
        var stepCount = plan.Mods.Operations.Count + 1;
        var modsDone = false;
        async Task InstallModsAsync(IProgress<InstallProgress>? reports, CancellationToken token)
        {
            await _executor.ExecuteAsync(plan.Mods, enable: true, Renumbered(progress, stepCount), stop, token).ConfigureAwait(false);
            await EnableAsync(instanceId, plan.Needed, token).ConfigureAwait(false);
            modsDone = true;
        }

        UserContentResult result;
        try
        {
            result = await stop.RunAsync(
                (reports, token) => plan.Installed is { } installed
                    ? _installer.UpdateAsync(instanceId, installed, plan.Release, plan.Check.FolderName, plan.DiscardChanges, InstallModsAsync, reports, token)
                    : _installer.InstallAsync(instanceId, plan.Release, plan.Check.FolderName, InstallModsAsync, reports, token),
                progress.ForStep(1, stepCount),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (modsDone && exception is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Borea did not put '{plan.Release.ModId}' into place, and the mods it needs stay installed. {exception.Message}", exception);
        }

        if (modsDone && !result.IsDone)
            throw new InvalidOperationException($"Borea did not put '{plan.Release.ModId}' into place ({result.Outcome}, folder '{result.FolderName}'), and the mods it needs stay installed.");

        return result;
    }

    private async Task<UserContentPlan> PlanAsync(UserContentRequest request, InstalledContentRecord? installed, UserContentResult check, bool discardChanges, CancellationToken cancellationToken)
    {
        var mods = await _planner.PlanAsync(
            new InstallPlanningRequest(request.Instance, [new RequestedMod(request.Release, InstallReason.Manual, Exact: true)], request.Repository, request.GameVersion, request.TargetPlatform, request.Recommended, request.Alternatives, request.Channel),
            cancellationToken).ConfigureAwait(false);
        var needed = Needed(request.Instance, request.Release, mods);
        var entries = await _modState.GetEntriesAsync(request.Instance.InstanceId, cancellationToken).ConfigureAwait(false);
        var enables = needed
            .Where(id => IsInstalled(request.Instance, id))
            .Where(id => !entries.Any(entry => entry.Enabled && ModIds.Equals(entry.ModId, id)))
            .ToList();
        return new UserContentPlan(request.Release, installed, mods, needed, enables, check, discardChanges);
    }

    /// <summary>
    /// Every mod the plan selects, and every installed mod that a required dependency of the item or of a selected mod
    /// names, because the game treats a mod that is not enabled as missing (ModLibrary.PrepareAll). For an any_of the
    /// alternative the plan resolved comes first, because the planner does not select an installed alternative.
    /// </summary>
    private static List<string> Needed(Instance instance, ModVersionMetadata release, InstallPlan mods)
    {
        var needed = mods.Selections.Select(selection => selection.Release.ModId).ToList();
        foreach (var owner in mods.Selections.Select(selection => selection.Release).Prepend(release))
        {
            foreach (var dependency in owner.Dependencies.Where(value => value.Kind == ModDependencyKind.Required))
            {
                var targets = dependency.IsAnyOf ? dependency.AnyOf.Select(value => value.ModId) : [dependency.ModId!];
                var present = targets.Where(id => needed.Contains(id, ModIds.Comparer) || IsInstalled(instance, id)).ToList();
                if (present.Count == 0 || present.Any(id => needed.Contains(id, ModIds.Comparer)))
                    continue;
                var resolved = mods.Choices.FirstOrDefault(choice => choice.Kind == PlanningChoiceKind.Alternative && choice.Dependency == dependency && ModIds.Equals(choice.OwnerModId, owner.ModId))?.Selected;
                needed.Add(present.FirstOrDefault(id => ModIds.Equals(id, resolved)) ?? present[0]);
            }
        }

        return needed.Distinct(ModIds.Comparer).ToList();
    }

    private static bool IsInstalled(Instance instance, string id) =>
        instance.Mods.Any(mod => ModIds.Equals(mod.ModId, id)) || instance.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, id));

    /// <summary>A new entry goes in enabled, and an entry that exists is enabled, because <see cref="IModStateRepository.AddEntryAsync"/> keeps a disabled one.</summary>
    private async Task EnableAsync(Guid instanceId, IReadOnlyList<string> needed, CancellationToken cancellationToken)
    {
        var entries = await _modState.GetEntriesAsync(instanceId, cancellationToken).ConfigureAwait(false);
        foreach (var id in needed)
        {
            if (!entries.Any(entry => ModIds.Equals(entry.ModId, id)))
                await _modState.AddEntryAsync(instanceId, id, enabled: true, cancellationToken).ConfigureAwait(false);
            else if (!entries.Any(entry => entry.Enabled && ModIds.Equals(entry.ModId, id)))
                await _modState.SetActiveAsync(instanceId, id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The mods are steps 2 and on, after the folder of the item.</summary>
    private static IProgress<InstallProgress>? Renumbered(IProgress<InstallProgress>? progress, int stepCount) =>
        progress is null ? null : new SynchronousProgress<InstallProgress>(value => progress.Report(value with { Step = value.Step + 1, StepCount = stepCount }));

    private static void Validate(UserContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Instance);
        ArgumentNullException.ThrowIfNull(request.Release);
        ArgumentNullException.ThrowIfNull(request.Repository);
        if (!request.Release.Type.IsVehicleOrSave())
            throw new ArgumentException($"'{request.Release.ModId}' is not a vehicle or a save.", nameof(request));
    }
}
