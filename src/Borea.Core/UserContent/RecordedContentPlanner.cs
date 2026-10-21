using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.State;

namespace Borea.Core.UserContent;

/// <summary>
/// Adds a warning to a plan for each recorded vehicle or save that the plan breaks, see
/// <see cref="RecordedContentNeeds"/>. The warnings come after the search, so they never change which
/// releases the plan picks, and they never stop a plan, because only an incompatible release blocks.
/// </summary>
public sealed class RecordedContentPlanner : IInstallPlanner
{
    private readonly IInstalledContentRecords _records;
    private readonly IModStateRepository? _modState;

    public IInstallPlanner Inner { get; }

    /// <param name="modState">Tells which mods the instance disables. Null counts every installed mod as loaded.</param>
    public RecordedContentPlanner(IInstallPlanner inner, IInstalledContentRecords records, IModStateRepository? modState = null)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _modState = modState;
    }

    public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await Inner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (plan.Operations.Count == 0 && request.Current is null)
            return plan;

        IReadOnlyList<InstalledContentRecord> records;
        IReadOnlyList<string> inactive;
        try
        {
            records = await _records.ListAsync(request.Instance.InstanceId, cancellationToken).ConfigureAwait(false);
            if (records.Count == 0)
                return plan;

            inactive = _modState is null ? [] : (await _modState.GetEntriesAsync(request.Instance.InstanceId, cancellationToken).ConfigureAwait(false))
                .Where(entry => !entry.Enabled)
                .Select(entry => entry.ModId)
                .ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A record or manifest file Borea cannot read gives no warning, so it never stops a plan.
            return plan;
        }

        // An update of the item itself replaces its record, so the old release does not count.
        var kept = records.Where(record => !request.Requested.Any(requested => ModIds.Equals(requested.Release.ModId, record.ListingId))
            && !(request.ReplacedContent?.Contains(record.ListingId) ?? false));
        var before = RecordedContentNeeds.InstanceView(request.Current ?? request.Instance, inactive);
        var after = RecordedContentNeeds.With(RecordedContentNeeds.InstanceView(request.Instance, inactive), plan.Operations);
        var broken = RecordedContentNeeds.Broken(kept, before, after);
        return plan.WithWarnings(broken.Select(item => Warning(item, plan.Operations, after)).ToList());
    }

    // The mod that the plan updates, or, in a run that removes mods first, the one it removes.
    private static PlanningMessage Warning(BrokenContent broken, IReadOnlyList<PlannedInstall> operations, IReadOnlyDictionary<string, ModVersion?> after)
    {
        var dependency = broken.Dependency;
        var ids = dependency.IsAnyOf ? dependency.AnyOf.Select(alternative => alternative.ModId).ToList() : [dependency.ModId];
        var release = operations.FirstOrDefault(operation => ids.Any(id => ModIds.Equals(id, operation.Release.ModId)))?.Release;
        var kind = broken.Record.Type == ContentType.Save ? PlanningMessageKind.BreaksSave : PlanningMessageKind.BreaksVehicle;
        return new PlanningMessage(release?.ModId ?? ids.FirstOrDefault(id => !after.ContainsKey(id)) ?? ids[0], kind) { Version = release?.Version, Dependency = dependency, Value = broken.Record.FolderName };
    }
}
