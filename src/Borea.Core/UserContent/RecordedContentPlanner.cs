using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.UserContent;

/// <summary>
/// Adds a warning to a plan for each recorded vehicle or save that the plan breaks, see
/// <see cref="RecordedContentNeeds"/>. The warnings come after the search, so they never change which
/// releases the plan picks, and they never stop a plan, because only an incompatible release blocks.
/// </summary>
public sealed class RecordedContentPlanner : IInstallPlanner
{
    private readonly IInstalledContentRecords _records;

    public IInstallPlanner Inner { get; }

    public RecordedContentPlanner(IInstallPlanner inner, IInstalledContentRecords records)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _records = records ?? throw new ArgumentNullException(nameof(records));
    }

    public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await Inner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (plan.Operations.Count == 0)
            return plan;

        IReadOnlyList<InstalledContentRecord> records;
        try
        {
            records = await _records.ListAsync(request.Instance.InstanceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A record file Borea cannot read gives no warning, so it never stops a plan.
            return plan;
        }

        // An update of the item itself replaces its record, so the old release does not count.
        var kept = records.Where(record => !request.Requested.Any(requested => ModIds.Equals(requested.Release.ModId, record.ListingId)));
        var before = RecordedContentNeeds.InstanceView(request.Instance);
        var broken = RecordedContentNeeds.Broken(kept, before, RecordedContentNeeds.With(before, plan.Operations));
        return plan.WithWarnings(broken.Select(item => Warning(item, plan.Operations)).ToList());
    }

    private static PlanningMessage Warning(BrokenContent broken, IReadOnlyList<PlannedInstall> operations)
    {
        var dependency = broken.Dependency;
        var ids = dependency.IsAnyOf ? dependency.AnyOf.Select(alternative => alternative.ModId).ToList() : [dependency.ModId];
        var release = operations.First(operation => ids.Any(id => ModIds.Equals(id, operation.Release.ModId))).Release;
        var kind = broken.Record.Type == ContentType.Save ? PlanningMessageKind.BreaksSave : PlanningMessageKind.BreaksVehicle;
        return new PlanningMessage(release.ModId, kind) { Version = release.Version, Dependency = dependency, Value = broken.Record.FolderName };
    }
}
