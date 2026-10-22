using Borea.Core.Game;
using Borea.Core.Instances;

namespace Borea.Cli.Commands;

/// <summary>
/// Finds the instance a command line names.
/// </summary>
internal static class InstanceLookup
{
    /// <summary>
    /// The instance named by its display name, compared case-insensitively, or by
    /// its id. When two names differ only in case, only the id names one of them.
    /// </summary>
    public static async Task<Instance> ResolveAsync(IInstanceRepository instances, string nameOrId)
    {
        var isId = Guid.TryParse(nameOrId, out var instanceId);
        if (isId)
        {
            var byId = await instances.GetByIdAsync(instanceId).ConfigureAwait(false);
            if (byId is not null)
                return byId;
        }

        var all = await instances.GetAllAsync().ConfigureAwait(false);
        var named = all
            .Where(instance => string.Equals(instance.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return named.Count switch
        {
            1 => named[0],
            0 when isId => throw new InvalidOperationException($"No instance has the id '{nameOrId}', and none is named that."),
            0 => throw new InvalidOperationException($"No instance is named '{nameOrId}'."),
            _ => throw new InvalidOperationException(
                $"More than one instance is named '{nameOrId}' when compared case-insensitively. Name it by id: " +
                string.Join(", ", named.Select(instance => $"{instance.Name} ({instance.InstanceId})")) + "."),
        };
    }

    /// <summary>
    /// The instance a command acts on: the one named, or the active one when
    /// none was named.
    /// </summary>
    public static async Task<Instance> ResolveTargetAsync(IInstanceRepository instances, string? nameOrId)
    {
        if (nameOrId is not null)
            return await ResolveAsync(instances, nameOrId).ConfigureAwait(false);

        var activeId = await instances.GetActiveInstanceIdAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("No instance is active. Pass --instance, or run 'borea instance activate'.");

        return await instances.GetByIdAsync(activeId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The active instance {activeId} does not exist anymore. Activate another one.");
    }

    /// <summary>
    /// The game version a command checks releases for the instance against: its kept build, or the game folder of
    /// the settings when it keeps none. A kept build that cannot be used stops the command, because an unknown game
    /// only warns in the planner, which would then pick the newest release.
    /// </summary>
    public static GameVersion? GameVersionOf(CliServices cli, Instance instance)
    {
        var game = cli.InstanceGames.Resolve(instance);
        if (game.Problem == KeptBuildProblem.None)
            return game.Version?.Version;

        throw new InvalidOperationException(
            $"Instance '{instance.Name}' keeps game build {game.KeptBuild}, but {DescribeKeptBuildProblem(cli, game)}. " +
            "Copy that build back into the folder. To use the game folder of the settings instead, select \"Use the current game build\" in the instance menu of the desktop App, or remove GameBuild from the instance.toml of the instance.");
    }

    /// <summary>Why the kept build of <paramref name="game"/> cannot be used, for a game that has a problem.</summary>
    public static string DescribeKeptBuildProblem(CliServices cli, InstanceGame game)
    {
        var build = game.KeptBuild ?? throw new ArgumentException("Only a kept build can have a problem.", nameof(game));
        var folder = cli.Paths.GetGameBuildFolder(build);
        return game.Problem == KeptBuildProblem.Missing
            ? $"its folder {folder} is gone"
            : $"the KSA.dll in {folder} is missing or reads another version";
    }
}
