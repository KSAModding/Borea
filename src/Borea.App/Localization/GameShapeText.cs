using System.Collections.Generic;
using System.Linq;
using Borea.Core.Game;

namespace Borea.App.Localization;

/// <summary>
/// The assumptions Borea makes about the game, in the display language.
/// </summary>
internal static class GameShapeText
{
    public static string Assumption(GameAssumption assumption) => assumption switch
    {
        GameAssumption.GameAssembly => Resources.GameAssumptionGameAssembly,
        GameAssumption.ContentManifest => Resources.GameAssumptionContentManifest,
        GameAssumption.PatchNotes => Resources.GameAssumptionPatchNotes,
        GameAssumption.ProfileLayout => Resources.GameAssumptionProfileLayout,
        GameAssumption.ProfileManifest => Resources.GameAssumptionProfileManifest,
        GameAssumption.ModFolder => Resources.GameAssumptionModFolder,
        GameAssumption.SessionLog => Resources.GameAssumptionSessionLog,
        _ => assumption.ToString(),
    };

    /// <summary>The broken assumptions in one line, for the banner.</summary>
    public static string Broken(IEnumerable<GameAssumptionResult> results)
        => string.Join(", ", results.Select(result => Assumption(result.Assumption)));
}
