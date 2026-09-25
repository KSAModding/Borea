using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>What the API of a release host says about the content and its latest release.</summary>
/// <param name="Name">The display name the host shows, or null.</param>
/// <param name="License">The license as the host names it, which need not be an SPDX expression.</param>
/// <param name="Links">The links the host knows, keyed like [links].</param>
/// <param name="Latest">The latest release, or null when the host has none with a version the index can read.</param>
public sealed record ListingHostFacts(
    ListingSourceReference Source,
    string? Name,
    string? Abstract,
    string? License,
    IReadOnlyList<string> Authors,
    IReadOnlyList<ListingLink> Links,
    ListingReleases Releases,
    ListingHostRelease? Latest);

/// <param name="DownloadUrl">The archive, or null when the release has several archives and none is named after the content.</param>
/// <param name="Candidates">The archives of a release whose archive could not be chosen.</param>
public sealed record ListingHostRelease(string Tag, string Version, string? DownloadUrl, long? SizeBytes, IReadOnlyList<string> Candidates);

/// <summary>Reads a release host's API.</summary>
public interface IListingHostClient
{
    /// <exception cref="ListingSourceException">The host has no such repository or mod.</exception>
    /// <exception cref="HttpRequestException">The host did not answer.</exception>
    Task<ListingHostFacts> ReadAsync(ListingSourceReference source, CancellationToken cancellationToken = default);
}

/// <summary>What the latest release archive shows, read with the rules the stamper of content-index-releases uses.</summary>
/// <param name="Root">The install root the stamper derives for a mod: the one top-level folder, or null when there is none to derive.</param>
/// <param name="TopLevelFolders">The folders at the top of the archive, in archive order.</param>
/// <param name="ModToml">Whether the root holds a mod.toml.</param>
/// <param name="EntryAssembly">The assembly StarMap loads: [StarMap] EntryAssembly of the mod.toml, else the root name.</param>
/// <param name="IsCodeMod">Whether the root holds the <see cref="EntryAssembly"/> DLL.</param>
public sealed record ListingArchiveFacts(string? Root, IReadOnlyList<string> TopLevelFolders, bool ModToml, string? EntryAssembly, bool IsCodeMod);

/// <param name="Archive">The facts of the latest release archive, or null when there was none to read.</param>
/// <param name="ArchiveProblem">Why the archive could not be read, or null.</param>
public sealed record ListingSource(ListingHostFacts Host, ListingArchiveFacts? Archive, string? ArchiveProblem);

/// <summary>Reads a release host and the archive of its latest release, which it downloads into a temporary file and deletes again.</summary>
public interface IListingSourceReader
{
    /// <summary>The largest archive the checks of content-index accept.</summary>
    public const long MaxArchiveBytes = 4L * 1024 * 1024 * 1024;

    /// <exception cref="ListingSourceException">The host has no such repository or mod.</exception>
    /// <exception cref="HttpRequestException">The host did not answer.</exception>
    Task<ListingSource> ReadAsync(ListingSourceReference source, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class ListingSourceException(string message) : Exception(message);
