namespace Borea.Core.Preview;

/// <summary>
/// Something the preview tells the player, for example why a part shows as a box. <paramref name="Subject"/> is the
/// Id, path or mod Id the notice is about, and <paramref name="Detail"/> is an optional technical message for the log.
/// </summary>
public sealed record PreviewNotice(PreviewNoticeKind Kind, string Subject, string? Detail = null);

public enum PreviewNoticeKind
{
    /// <summary>Two items have the same Id, also when the Ids differ only by case. The game keeps the first one, and so does the preview.</summary>
    DuplicateId,

    /// <summary>A needed mod has patches in its mod.toml, which the preview does not apply.</summary>
    PatchMod,

    /// <summary>A mesh or texture file could not be read or decoded.</summary>
    AssetFailed,

    /// <summary>A texture has a format the preview cannot decode, so it uses a flat color.</summary>
    UnsupportedTexture,

    /// <summary>The scene went over a triangle, draw or texture budget, so some items show as boxes or flat colors.</summary>
    Budget,

    /// <summary>A subpart refers back to itself or nests too deep.</summary>
    Cycle,

    /// <summary>A mod source that the vehicle needs is not on disk.</summary>
    MissingSource,

    /// <summary>The install planner failed, so the needed mods come from the required dependencies of the release.</summary>
    PlannerFallback,
}
