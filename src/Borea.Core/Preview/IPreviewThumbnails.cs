using Borea.Core.Game;

namespace Borea.Core.Preview;

/// <summary>
/// The still images of vehicle previews that Discover shows, one for each vehicle archive and game build. The vehicle
/// page stores a still only after its preview showed the whole vehicle, and Discover only looks one up, so a listed
/// vehicle costs Discover no fetch, no part read and no plan.
/// </summary>
public interface IPreviewThumbnails
{
    /// <summary>
    /// The path of the PNG still of this vehicle archive for this game build, or null when there is none or when the
    /// arguments cannot name a still. It checks for one file and changes nothing on the disk.
    /// </summary>
    /// <param name="vehicleSha256">The SHA-256 of the vehicle archive that the release names.</param>
    /// <param name="gameBuild">The installed game build, as <see cref="PreviewGameBuild.Of"/> gives it.</param>
    string? TryGetPath(string? vehicleSha256, string? gameBuild);

    /// <summary>
    /// True when no still is stored for this vehicle archive and game build, or when the stored one was made from
    /// another scene, for example before the player installed a mod that changes a part. False when the arguments
    /// cannot name a still, because then <see cref="Store"/> cannot keep one either.
    /// </summary>
    /// <param name="sceneKey">The <see cref="VehiclePreviewResult.SceneKey"/> of the scene that the still would show.</param>
    bool NeedsUpdate(string? vehicleSha256, string? gameBuild, string? sceneKey);

    /// <summary>
    /// Keeps the PNG bytes as the still of this vehicle archive and game build, in place of an older still of the same
    /// pair, and then removes the least recently stored stills over the size limit. A failure to write is logged and
    /// not thrown, because without a still Discover shows the icon of the listing.
    /// </summary>
    /// <exception cref="ArgumentException">An argument cannot name a still, or the bytes are empty or too large.</exception>
    void Store(string vehicleSha256, string gameBuild, string sceneKey, byte[] pngBytes);
}

/// <summary>The game build text that names a stored still and goes into the scene key of a preview.</summary>
public static class PreviewGameBuild
{
    /// <summary>The parsed build, or the raw version text when it does not parse, or null when no build was found.</summary>
    public static string? Of(InstalledGameVersion? installed) => installed is null ? null : installed.Version?.ToString() ?? installed.RawVersion;
}
