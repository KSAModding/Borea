using Borea.Core.Game;

namespace Borea.Cli.Output;

/// <summary>Shapes the result of the game check for text and JSON output.</summary>
internal static class GameShapeOutput
{
    /// <summary>
    /// The one line a command writes before it starts the game. A launch only
    /// warns, because a wrong assumption costs a game that starts without mods
    /// and not a file written in the wrong format. A build nobody checked yet
    /// is the normal state between a game patch and the next Borea release, so
    /// it is reported by <c>game check</c> and not at every launch.
    /// </summary>
    public static void WriteWarning(GameShape shape, TextWriter error)
    {
        if (shape.Status != GameShapeStatus.Broken)
            return;

        error.WriteLine(
            $"warning: {shape.Subject} does not have the shape Borea expects, so the game may start without the mods. "
            + $"{shape.BrokenText} Run 'borea game check' for the whole list.");
    }

    public static GameShapeView View(GameShape shape) => new(
        shape.Status.ToString(),
        shape.Version?.Version?.ToString(),
        shape.Version?.RawVersion,
        shape.VerifiedBuilds.Newest.ToString(),
        [.. shape.Results.Select(result => new GameAssumptionView(result.Assumption.ToString(), result.State.ToString(), result.Detail))]);
}

/// <summary>The JSON shape of <c>game check</c>.</summary>
internal sealed record GameShapeView(
    string Status,
    string? Build,
    string? RawBuild,
    string NewestVerifiedBuild,
    IReadOnlyList<GameAssumptionView> Assumptions);

internal sealed record GameAssumptionView(string Assumption, string State, string Detail);
