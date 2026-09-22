namespace Borea.Core.Game;

/// <summary>
/// What the check found in the installation. Borea warns before a launch on a
/// shape that is not <see cref="GameShapeStatus.Verified"/>, and refuses to
/// write when it is <see cref="GameShapeStatus.Broken"/>.
/// </summary>
/// <param name="Version">The installed build, or null when none was found.</param>
public sealed record GameShape(InstalledGameVersion? Version, VerifiedGameBuilds VerifiedBuilds, IReadOnlyList<GameAssumptionResult> Results)
{
    public IReadOnlyList<GameAssumptionResult> Broken { get; } = Results.Where(result => result.IsBroken).ToList();

    public GameShapeStatus Status =>
        Broken.Count > 0 ? GameShapeStatus.Broken
        : Results.All(result => result.State == GameAssumptionState.NotChecked) ? GameShapeStatus.Unknown
        : Version?.Version is { } build && VerifiedBuilds.IsPastNewest(build) ? GameShapeStatus.Untested
        : GameShapeStatus.Verified;

    /// <summary>
    /// Whether Borea may write into the game's files. A wrong shape costs a
    /// corrupted manifest or a mod in a folder the game no longer reads, so a
    /// write stops where a launch only warns. A break in an assumption Borea
    /// only reads through costs information and stops nothing.
    /// </summary>
    public bool AllowsWrites => !Broken.Any(result => WriteDepends(result.Assumption));

    /// <summary>
    /// Whether a write depends on the assumption. Which of them were checked
    /// follows from the subject the shape is about, because the layout of a
    /// profile is only evidence in the profile the game creates itself.
    /// </summary>
    public static bool WriteDepends(GameAssumption assumption) => assumption
        is GameAssumption.ContentManifest
        or GameAssumption.ProfileLayout
        or GameAssumption.ProfileManifest
        or GameAssumption.ModFolder;

    /// <summary>The build for a message, which is the raw string when it did not parse.</summary>
    public string BuildText => Version?.Version?.ToString() ?? Version?.RawVersion ?? "unknown";

    /// <summary>What a message calls the installation, which names the build only when there is one.</summary>
    public string Subject => Version is null ? "This KSA installation" : $"KSA {BuildText}";

    /// <summary>One line naming what is broken, empty when nothing is.</summary>
    public string BrokenText => string.Join(" ", Broken.Select(result => result.Detail));
}
