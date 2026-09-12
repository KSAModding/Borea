using Borea.Core.Mods;

namespace Borea.Cli.Commands;

internal static class LoaderLookup
{
    public static async Task<ModMetadata> GetListingAsync(
        IModRepository repository,
        string loaderId,
        CancellationToken cancellationToken)
    {
        var listings = await repository.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        var listing = listings.FirstOrDefault(candidate => ModIds.Equals(candidate.ModId, loaderId));
        if (listing is null)
            throw new InvalidOperationException($"Mod loader '{loaderId}' is not available from the configured sources.");

        if (listing.Type != ContentType.ModLoader)
            throw new InvalidOperationException($"'{listing.ModId}' is a {listing.Type}, not a mod loader.");

        return listing;
    }

    public static async Task<IReadOnlyList<ModVersionMetadata>> GetReleasesAsync(
        IModRepository repository,
        string loaderId,
        CancellationToken cancellationToken)
    {
        var versions = await repository.GetAvailableVersionsAsync(loaderId, cancellationToken).ConfigureAwait(false);
        var releases = new List<ModVersionMetadata>(versions.Count);
        foreach (var version in versions)
        {
            var release = await repository.GetReleaseAsync(loaderId, version, cancellationToken).ConfigureAwait(false);
            if (release is not null)
                releases.Add(release);
        }

        return releases;
    }
}
