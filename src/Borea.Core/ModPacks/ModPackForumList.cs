using Borea.Core.Mods;

namespace Borea.Core.ModPacks;

/// <summary>
/// The member list that the forum rules ask a pack thread for: one line per mod with its name,
/// version, author, license, download link and release thread, in pack order.
/// The lines are English plain text, because the forum is English and a plain line with bare
/// links reads the same whether the forum renders BBCode or not. The share page writes the same lines.
/// </summary>
public static class ModPackForumList
{
    public static async Task<IReadOnlyList<string>> WriteAsync(ModPackMetadata pack, IModRepository mods, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(mods);

        var lines = new List<string>();
        foreach (var pin in pack.Mods)
        {
            var release = await mods.GetReleaseAsync(pin.ContentId, pin.Version, cancellationToken).ConfigureAwait(false);
            var listing = release is null ? null : await mods.GetListingAsync(pin.ContentId, cancellationToken).ConfigureAwait(false);
            lines.Add(release is null || listing is null
                ? $"{pin.ContentId} {pin.Version} - Not listed in the content index"
                : $"{listing.Name} {pin.Version} - Author: {string.Join(", ", listing.Authors)} - License: {listing.License} - Download: {release.Download.Url} - Thread: {listing.ForumUrl}");
        }

        return lines;
    }
}
