using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.UserContent;

namespace Borea.Storage.ModPacks;

/// <summary>
/// The pinned vehicles and saves of a pack install or update (RFC 0085). Each one installs at its exact version
/// after the mods, without planning its dependencies, because the pack makes the mods it needs members (RFC 0080).
/// A pin that cannot install is skipped with a warning, and a taken name or a changed item waits for the player.
/// </summary>
internal sealed class ModPackUserContent(IUserContentInstaller? installer)
{
    private const string StoppedMessage = "The pack stopped before this step.";

    /// <summary>One pin and what the run does with it.</summary>
    public sealed record Step(ModPackContentResult Planned, ModVersionMetadata? Release, InstalledContentRecord? Installed);

    /// <summary>The checks of every pin, without a write or a download.</summary>
    public async Task<IReadOnlyList<Step>> PlanAsync(Guid instanceId, ModPackMetadata pack, IUserContentRepository? repository, GameVersion? gameVersion, IReadOnlyDictionary<string, string>? names, IReadOnlySet<string>? discard, List<PlanningMessage> warnings, CancellationToken cancellationToken)
    {
        var steps = new List<Step>();
        foreach (var (type, pin) in pack.Vehicles.Select(pin => (ContentType.Vehicle, pin)).Concat(pack.Saves.Select(pin => (ContentType.Save, pin))))
            steps.Add(await PlanAsync(instanceId, type, pin, repository, gameVersion, names, discard, warnings, cancellationToken).ConfigureAwait(false));

        return steps;
    }

    /// <summary>Runs every step that can run, one after another. A failure stops only its own step.</summary>
    public async Task<IReadOnlyList<ModPackContentResult>> RunAsync(Guid instanceId, IReadOnlyList<Step> steps, IReadOnlyDictionary<string, string>? names, IReadOnlySet<string>? discard, IProgress<InstallProgress>? progress, int firstStep, int stepCount, InstallStop? stop, CancellationToken cancellationToken)
    {
        var results = new List<ModPackContentResult>();
        var step = firstStep;
        foreach (var item in steps)
        {
            if (item.Planned.Status != ModPackContentStatus.NotAttempted || item.Release is null)
            {
                results.Add(item.Planned);
                continue;
            }

            if (stop is { IsRequested: true })
            {
                results.Add(item.Planned with { Message = StoppedMessage });
                continue;
            }

            var name = Name(names, item.Planned.ContentId);
            var reports = progress.ForStep(step++, stepCount);
            try
            {
                var result = await stop.RunAsync(
                    (relay, token) => item.Installed is { } installed
                        ? installer!.UpdateAsync(instanceId, installed, item.Release, name, Discards(discard, item.Planned.ContentId), progress: relay, cancellationToken: token)
                        : installer!.InstallAsync(instanceId, item.Release, name, progress: relay, cancellationToken: token),
                    reports,
                    cancellationToken).ConfigureAwait(false);
                results.Add(From(item.Planned, result));
            }
            catch (InstallStoppedException)
            {
                results.Add(item.Planned with { Message = StoppedMessage });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(item.Planned with { Status = ModPackContentStatus.Failed, Message = exception.Message });
            }
        }

        return results;
    }

    /// <summary>The planned steps as the result of a run that did not reach them.</summary>
    public static IReadOnlyList<ModPackContentResult> NotReached(IReadOnlyList<Step> steps, string message) =>
        steps.Select(item => item.Planned.Status == ModPackContentStatus.NotAttempted ? item.Planned with { Message = message } : item.Planned).ToList();

    /// <summary>How many steps a run installs or updates.</summary>
    public static int Count(IReadOnlyList<Step> steps) => steps.Count(item => item.Planned.Status == ModPackContentStatus.NotAttempted && item.Release is not null);

    private async Task<Step> PlanAsync(Guid instanceId, ContentType type, ModPackEntry pin, IUserContentRepository? repository, GameVersion? gameVersion, IReadOnlyDictionary<string, string>? names, IReadOnlySet<string>? discard, List<PlanningMessage> warnings, CancellationToken cancellationToken)
    {
        var planned = new ModPackContentResult(type, pin.ContentId, pin.Version, ModPackContentStatus.NotAttempted);
        if (installer is null || repository is null)
            return new Step(planned with { Status = ModPackContentStatus.Skipped, Message = "Borea has no source of vehicles and saves for this pack." }, null, null);

        var release = await repository.GetReleaseAsync(pin.ContentId, pin.Version, cancellationToken).ConfigureAwait(false);
        PlanningMessage? warning = null;
        if (release is null || release.Type != type)
            warning = new PlanningMessage(pin.ContentId, PlanningMessageKind.UnlistedPin);
        else if (release.Yanked)
            warning = new PlanningMessage(pin.ContentId, PlanningMessageKind.YankedPin) { Value = release.YankedReason };
        else if (release.Download.UnavailableSince is { } since)
            warning = new PlanningMessage(pin.ContentId, PlanningMessageKind.Unavailable) { Version = release.Version, Since = since };
        else if (Compatibility.IsAboveUpperBound(release, gameVersion))
            warning = new PlanningMessage(pin.ContentId, PlanningMessageKind.AboveGameMax) { Value = release.GameMax };
        else if (Compatibility.Evaluate(release, gameVersion) == GameCompatibility.Incompatible)
            warning = new PlanningMessage(pin.ContentId, PlanningMessageKind.Incompatible) { Value = release.GameMin };

        if (warning is not null)
        {
            warnings.Add(warning);
            return new Step(planned with { Status = ModPackContentStatus.Skipped, Message = warning.Message, Warning = warning }, null, null);
        }

        var name = Name(names, pin.ContentId);
        var check = await installer.CheckInstallAsync(instanceId, release!, name, cancellationToken).ConfigureAwait(false);
        InstalledContentRecord? installed = null;
        if (check.Outcome == UserContentOutcome.AlreadyInstalled)
        {
            installed = check.Record!;
            if (installed.Version == pin.Version)
                return new Step(planned with { Status = ModPackContentStatus.AlreadyInstalled, FolderName = installed.FolderName }, null, installed);

            check = await installer.CheckUpdateAsync(instanceId, installed, release!, name, Discards(discard, pin.ContentId), cancellationToken).ConfigureAwait(false);
        }

        return new Step(check.Outcome switch
        {
            UserContentOutcome.Ready => planned with { FolderName = check.FolderName },
            UserContentOutcome.NameTaken => planned with { Status = ModPackContentStatus.NameTaken, FolderName = check.FolderName, Message = $"An item named '{check.FolderName}' is in the way." },
            UserContentOutcome.Changed => planned with { Status = ModPackContentStatus.Changed, FolderName = check.FolderName, Changes = check.Changes, Message = "The player changed files of the item since the install." },
            UserContentOutcome.InvalidName => planned with { Status = ModPackContentStatus.NameTaken, FolderName = check.FolderName, Message = $"The game would change the name '{check.FolderName}'." },
            UserContentOutcome.GameRunning => planned with { Message = "KSA or StarMap is running." },
            _ => planned with { Status = ModPackContentStatus.Skipped, Message = check.Outcome.ToString() },
        }, release, installed);
    }

    private static ModPackContentResult From(ModPackContentResult planned, UserContentResult result) => result.Outcome switch
    {
        UserContentOutcome.Installed => planned with { Status = ModPackContentStatus.Installed, FolderName = result.FolderName },
        UserContentOutcome.Updated => planned with { Status = ModPackContentStatus.Updated, FolderName = result.FolderName },
        UserContentOutcome.NameTaken => planned with { Status = ModPackContentStatus.NameTaken, FolderName = result.FolderName, Message = $"An item named '{result.FolderName}' is in the way." },
        UserContentOutcome.Changed => planned with { Status = ModPackContentStatus.Changed, Changes = result.Changes, Message = "The player changed files of the item since the install." },
        _ => planned with { Status = ModPackContentStatus.Failed, Message = $"Borea did not install it ({result.Outcome})." },
    };

    private static string? Name(IReadOnlyDictionary<string, string>? names, string id) =>
        names?.FirstOrDefault(pair => ModIds.Equals(pair.Key, id)).Value;

    private static bool Discards(IReadOnlySet<string>? discard, string id) => discard?.Contains(id, ModIds.Comparer) ?? false;
}
