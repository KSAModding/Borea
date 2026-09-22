using Borea.Core.Mods;

namespace Borea.Core.Updates;

/// <summary>One published Borea release. <paramref name="Notes"/> is the Markdown release body.</summary>
public sealed record BoreaRelease(ModVersion Version, string Tag, string PageUrl, string? Notes = null, DateTimeOffset? PublishedAt = null)
{
    /// <summary>The files published with the release, the platform archives and the checksums among them.</summary>
    public IReadOnlyList<BoreaReleaseAsset> Assets { get; init; } = [];

    /// <summary>
    /// True when this release is newer than <paramref name="runningVersion"/>, ignoring build metadata.
    /// A running version that does not parse is never older.
    /// </summary>
    public bool IsNewerThan(string? runningVersion)
        => ModVersion.TryParse(runningVersion, out var running) && Version > running;

    /// <summary>Reads a release tag, with one leading "v" removed.</summary>
    public static bool TryParseTag(string? tag, out ModVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(tag))
            return false;

        return ModVersion.TryParse(tag.StartsWith('v') ? tag[1..] : tag, out version);
    }
}
