using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.State;

namespace Borea.Core.Listings;

/// <summary>Why a mod of an instance is not a member of the pack made from that instance.</summary>
public enum ListingLeftOutReason
{
    /// <summary>A folder that Borea did not install, so no release is known for it.</summary>
    NotInstalledByBorea = 0,

    /// <summary>A pack pins mods only.</summary>
    ModLoader = 1,

    /// <summary>A disabled mod is not part of the set that was tested.</summary>
    Disabled = 2,

    /// <summary>The snapshot has no listing of type mod for it, or the listing is delisted.</summary>
    NotListed = 3,

    /// <summary>The installed release is yanked in the index.</summary>
    Yanked = 4,

    /// <summary>The listing has no stamped release of the installed version.</summary>
    ReleaseNotListed = 5,

    /// <summary>The download of the installed release is gone from its host (RFC 0078), so another player could not install the pin.</summary>
    DownloadGone = 6,

    /// <summary>The player changed the vehicle or save since Borea installed it, so the pinned release is not what the instance holds.</summary>
    ChangedSinceInstall = 7,
}

/// <summary>
/// A mod, vehicle or save of the instance that the pack leaves out. <see cref="Version"/> is null for a folder Borea did not install,
/// and for a loader whose installed version Borea does not know. <see cref="Id"/> is the folder name of such a folder.
/// <see cref="GoneSince"/> is the time the download went away, for <see cref="ListingLeftOutReason.DownloadGone"/>.
/// </summary>
public sealed record ListingLeftOutMod(string Id, string? Version, ListingLeftOutReason Reason, DateTimeOffset? GoneSince = null, ContentType Type = ContentType.Mod);

/// <summary>A vehicle or save folder of the instance: its record and whether the player changed it, or no record for a folder Borea did not install.</summary>
public sealed record ListingInstalledContent(ContentType Type, string FolderName, InstalledContentRecord? Record, bool Changed);

/// <summary>
/// The members of a new pack made from an instance: the enabled mods that Borea installed from the index, at their
/// installed versions, in load order, and the vehicles and saves that Borea installed and the player did not change.
/// Every other mod of the instance is left out with its reason, and so is every loader that its mods need, because Borea
/// installs a loader outside the instance, and every vehicle or save folder that is not such a member.
/// </summary>
public sealed record ListingPackFromInstance(IReadOnlyList<ListingPackMember> Members, IReadOnlyList<ListingLeftOutMod> LeftOut)
{
    public IReadOnlyList<ListingPackMember> Vehicles { get; init; } = [];

    public IReadOnlyList<ListingPackMember> Saves { get; init; } = [];

    /// <param name="loaderInstallations">The loaders Borea set up, by id, which give the version of a needed loader.</param>
    /// <param name="content">The vehicle and save folders of the instance. Null leaves vehicles and saves out of the pack.</param>
    public static ListingPackFromInstance Of(
        ContentIndexSnapshot snapshot,
        Instance instance,
        IReadOnlyList<ModManifestEntry> manifest,
        IReadOnlyDictionary<string, LoaderInstallation>? loaderInstallations = null,
        IReadOnlyList<ListingInstalledContent>? content = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(instance);

        var installed = instance.Mods.ToDictionary(mod => mod.ModId, ModIds.Comparer);
        var members = new List<ListingPackMember>();
        var leftOut = new List<ListingLeftOutMod>();
        foreach (var entry in ModList.FromInstance(instance, manifest).Mods)
        {
            var version = entry.Version.ToString();
            var listing = ListingPackMembers.Listing(snapshot, entry.ModId);
            var release = listing?.Releases.FirstOrDefault(listed => listed.Version == entry.Version);
            ListingLeftOutReason? reason = IsModLoader(snapshot, installed[entry.ModId]) ? ListingLeftOutReason.ModLoader
                : !entry.Enabled ? ListingLeftOutReason.Disabled
                : listing is null ? ListingLeftOutReason.NotListed
                : release is null ? ListingLeftOutReason.ReleaseNotListed
                : release.Yanked ? ListingLeftOutReason.Yanked
                : release.Download.UnavailableSince is not null ? ListingLeftOutReason.DownloadGone
                : null;

            if (reason is { } found)
                leftOut.Add(new ListingLeftOutMod(entry.ModId, version, found, found == ListingLeftOutReason.DownloadGone ? release!.Download.UnavailableSince : null));
            else
                members.Add(new ListingPackMember(listing!.Id, version));
        }

        foreach (var loader in LoaderNeed.For(instance).Loaders)
        {
            if (leftOut.Any(mod => ModIds.Equals(mod.Id, loader.LoaderId)) || members.Any(member => ModIds.Equals(member.Id, loader.LoaderId)))
                continue;

            var installation = loaderInstallations?.FirstOrDefault(entry => ModIds.Equals(entry.Key, loader.LoaderId)).Value;
            leftOut.Add(new ListingLeftOutMod(loader.LoaderId, installation?.Version?.ToString() ?? installation?.RawVersion, ListingLeftOutReason.ModLoader));
        }

        leftOut.AddRange(instance.ForeignMods.Select(mod => new ListingLeftOutMod(mod.FolderName, null, ListingLeftOutReason.NotInstalledByBorea)));

        var pinned = new List<ListingPackMember>();
        foreach (var item in content ?? [])
        {
            if (item.Record is not { } record)
            {
                leftOut.Add(new ListingLeftOutMod(item.FolderName, null, ListingLeftOutReason.NotInstalledByBorea, Type: item.Type));
                continue;
            }

            var version = record.Version.ToString();
            var listing = ListingPackMembers.Listing(snapshot, record.ListingId, record.Type);
            var release = listing?.Releases.FirstOrDefault(listed => listed.Version == record.Version);
            ListingLeftOutReason? reason = item.Changed ? ListingLeftOutReason.ChangedSinceInstall
                : listing is null ? ListingLeftOutReason.NotListed
                : release is null ? ListingLeftOutReason.ReleaseNotListed
                : release.Yanked ? ListingLeftOutReason.Yanked
                : release.Download.UnavailableSince is not null ? ListingLeftOutReason.DownloadGone
                : null;

            if (reason is { } found)
                leftOut.Add(new ListingLeftOutMod(record.ListingId, version, found, found == ListingLeftOutReason.DownloadGone ? release!.Download.UnavailableSince : null, record.Type));
            else if (!pinned.Any(pin => ModIds.Equals(pin.Id, listing!.Id)))
                pinned.Add(new ListingPackMember(listing!.Id, version) { Type = record.Type });
        }

        return new ListingPackFromInstance(members, leftOut)
        {
            Vehicles = pinned.Where(pin => pin.Type == ContentType.Vehicle).ToList(),
            Saves = pinned.Where(pin => pin.Type == ContentType.Save).ToList(),
        };
    }

    private static bool IsModLoader(ContentIndexSnapshot snapshot, InstalledMod mod) =>
        mod.Metadata.Type == ContentType.ModLoader
        || snapshot.Listings.Any(listing => ModIds.Equals(listing.Id, mod.ModId) && listing.Authored?.Type == ContentType.ModLoader);
}
