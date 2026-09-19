using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Tags;

namespace Borea.Core.Listings;

/// <summary>Fills a new listing from what Borea can read: the release host, the latest archive, the content index and the installed game.</summary>
public static class ListingPrefill
{
    public const string StarMapId = "StarMap";

    /// <summary>
    /// The draft with every field the source knows filled in. A code mod gets a [loader] on StarMap from the
    /// newest stable StarMap release of the snapshot, and an empty game_min gets <see cref="DefaultGameMin"/>.
    /// </summary>
    public static ListingDraft Apply(ListingDraft draft, ListingSource source, ContentIndexSnapshot? snapshot, GameVersion? installedGame)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(source);

        var host = source.Host;
        var links = draft.Links.ToList();
        foreach (var link in host.Links)
        {
            var index = links.FindIndex(existing => string.Equals(existing.Key, link.Key, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                links.Add(link);
            else
                links[index] = link;
        }

        var root = source.Archive?.Root;
        var loader = draft.Loader;
        if (source.Archive is { IsCodeMod: true } && loader is null)
            loader = new ListingLoader(StarMapId, NewestStableVersion(snapshot, StarMapId) ?? string.Empty);

        return draft with
        {
            Id = root is not null && ModIds.IsValid(root) ? root : draft.Id,
            Name = Pick(host.Name, draft.Name),
            Authors = host.Authors.Count > 0 ? host.Authors : draft.Authors,
            Abstract = Pick(host.Abstract, draft.Abstract),
            License = Pick(host.License, draft.License),
            Links = links,
            Releases = host.Releases,
            Loader = loader,
            GameMin = string.IsNullOrEmpty(draft.GameMin) ? DefaultGameMin(installedGame, snapshot) ?? string.Empty : draft.GameMin,
        };
    }

    /// <summary>The installed game when Borea knows it, else the newest game version of the snapshot, written as the game displays it.</summary>
    public static string? DefaultGameMin(GameVersion? installedGame, ContentIndexSnapshot? snapshot)
    {
        if (installedGame is { } installed)
            return Bound(installed);

        var newest = snapshot?.GameVersions?.Versions
            .Select(text => GameVersion.TryParse(text, out var version) ? version : (GameVersion?)null)
            .OfType<GameVersion>()
            .OrderByDescending(version => version.Revision)
            .FirstOrDefault();
        return newest is { } version ? Bound(version) : null;
    }

    /// <summary>The newest release of <paramref name="id"/> in the snapshot that is stable and not yanked.</summary>
    public static string? NewestStableVersion(ContentIndexSnapshot? snapshot, string id)
    {
        var listing = snapshot?.Listings.FirstOrDefault(entry => ModIds.Equals(entry.Id, id));
        return listing?.Releases
            .Where(release => release.ReleaseStatus == ReleaseStatus.Stable && !release.Yanked)
            .Select(release => release.Version)
            .OrderDescending()
            .Select(version => (ModVersion?)version)
            .FirstOrDefault()?.ToString();
    }

    /// <summary>The curated tags whose forum prefix is one of <paramref name="prefixes"/>.</summary>
    public static IReadOnlyList<string> TagsFor(IReadOnlyList<string> prefixes, CuratedTagVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        ArgumentNullException.ThrowIfNull(vocabulary);

        return vocabulary.GetTags(ContentType.Mod)
            .Where(tag => tag.ForumPrefix is { } prefix && prefixes.Any(found => string.Equals(found.Trim(), prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(tag => tag.Tag)
            .ToList();
    }

    private static string Bound(GameVersion version) => $"{version.Year}.{version.Month}.{version.BuildNumber}.{version.Revision}";

    private static string Pick(string? found, string current) => string.IsNullOrWhiteSpace(found) ? current : found.Trim();
}
