using Borea.Core.Planning;

namespace Borea.Core.Logging;

public sealed class LoggingInstallPlanner : IInstallPlanner
{
    private readonly IBoreaLog _log;

    public IInstallPlanner Inner { get; }

    public LoggingInstallPlanner(IInstallPlanner inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instanceId = request.Instance.InstanceId;
        var requested = string.Join(", ", request.Requested.Select(mod => $"{mod.Release.ModId} {mod.Release.Version} ({mod.Reason})"));
        InstallPlan plan;
        try
        {
            plan = await Inner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Plan for instance {instanceId} cancelled. Requested: {requested}.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write($"Plan for instance {instanceId} failed. Requested: {requested}.", exception);
            throw;
        }

        var operations = plan.Operations.Count == 0
            ? "none"
            : string.Join(", ", plan.Operations.Select(operation => $"{operation.Release.ModId} {operation.Release.Version} ({operation.Reason})"));
        _log.Write($"Plan for instance {instanceId}: {(plan.IsReady ? "ready" : "not ready")}. Requested: {requested}. Operations: {operations}.");
        Write("warning", plan.Warnings);
        Write("conflict", plan.Conflicts);
        Write("unresolved choice", plan.UnresolvedChoices);
        return plan;
    }

    private void Write(string kind, IEnumerable<PlanningMessage> messages)
    {
        foreach (var message in messages)
            _log.Write($"Plan {kind} {message.ModId} ({message.Code}): {message.Message}");
    }
}
