using Borea.Core.Dependencies;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Adds a warning to a plan for each release it installs whose loader bounds no loader version can meet on the target game,
/// see <see cref="LoaderVersionChoice.CheckNeedAsync"/>. The warnings come after the search, so they never change which
/// releases the plan picks, and they never stop a plan, because a client warns about loader bounds and does not block (RFC 0031).
/// </summary>
public sealed class LoaderNeedPlanner : IInstallPlanner
{
    private readonly ReleaseChannel _channel;

    public IInstallPlanner Inner { get; }

    /// <param name="channel">The release channel of a request that names none.</param>
    public LoaderNeedPlanner(IInstallPlanner inner, ReleaseChannel channel)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _channel = channel;
    }

    public async Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await Inner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        var installed = plan.Operations.Select(operation => operation.Release).ToList();
        if (request.GameVersion is null || !installed.Any(release => release.Loader is not null))
            return plan;

        var warnings = new List<PlanningMessage>();
        try
        {
            foreach (var need in LoaderNeed.With(request.Instance, installed).Loaders)
            {
                var problem = await LoaderVersionChoice.CheckNeedAsync(request.Instance.Loader, need, request.Repository, request.Channel ?? _channel, request.GameVersion, cancellationToken).ConfigureAwait(false);
                foreach (var release in installed.Where(release => release.Loader is { } loader && ModIds.Equals(loader.LoaderId, need.LoaderId)))
                {
                    if (Warning(release, problem) is { } warning)
                        warnings.Add(warning);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException)
        {
            // An index Borea cannot read gives no warning, so it never stops a plan.
            return plan;
        }

        return plan.WithWarnings(warnings);
    }

    /// <summary>The warning about the loader bounds of <paramref name="release"/> for <paramref name="problem"/>, or null when the problem is none.</summary>
    public static PlanningMessage? Warning(ModVersionMetadata release, LoaderVersionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.Loader is not { } loader)
            return null;

        PlanningMessageKind? kind = problem switch
        {
            LoaderVersionProblem.OutsideModRange => PlanningMessageKind.LoaderNotAccepted,
            LoaderVersionProblem.NoRelease or LoaderVersionProblem.GameIncompatible => PlanningMessageKind.LoaderNotForGame,
            _ => null,
        };
        return kind is { } value
            ? new PlanningMessage(release.ModId, value) { Dependency = new ModDependency(loader.LoaderId, ModDependencyKind.Required, loader.MinVersion, loader.MaxVersion) }
            : null;
    }
}
