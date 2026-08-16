using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.ModLoaders;
using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// One stamped release of a mod or mod loader, mirroring the generated release file of RFC 0031.
/// </summary>
public sealed class ModVersionMetadata
{
    /// <summary>
    /// The version of the metadata specification.
    /// </summary>
    public int SpecVersion { get; }

    /// <summary>
    /// The mod's unique identifier.
    /// </summary>
    public string ModId { get; }

    /// <summary>
    /// The content type of the listing this release belongs to.
    /// </summary>
    public ContentType Type { get; }

    /// <summary>
    /// The release version, normalized to SemVer at stamp time.
    /// </summary>
    public ModVersion Version { get; }

    /// <summary>
    /// The versioning scheme, "semver" in spec version 1.
    /// </summary>
    public string VersionScheme { get; }

    /// <summary>
    /// The maturity of the release.
    /// </summary>
    public ReleaseStatus ReleaseStatus { get; }

    /// <summary>
    /// When the release appeared on its host, UTC.
    /// </summary>
    public DateTimeOffset ReleaseDate { get; }

    /// <summary>
    /// Oldest compatible game version as displayed, such as "2026.8.3.5117".
    /// </summary>
    public string GameMin { get; }

    /// <summary>
    /// The resolved revision of the lower bound.
    /// </summary>
    public int GameMinRevision { get; }

    /// <summary>
    /// Newest tested game version as displayed, if bounded.
    /// </summary>
    public string? GameMax { get; }

    /// <summary>
    /// The resolved revision of the upper bound, if bounded.
    /// </summary>
    public int? GameMaxRevision { get; }

    /// <summary>
    /// The platforms known to work, as stamped. Null means no known restriction.
    /// </summary>
    public IReadOnlyList<string>? Os { get; }

    /// <summary>
    /// Where the archive is and what it hashes to.
    /// </summary>
    public DownloadInfo Download { get; }

    /// <summary>
    /// Which source this release came from. Borea-internal, not a format field.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Unpacked size in bytes. Null when the source does not expose it.
    /// </summary>
    public long? InstallSizeBytes { get; }

    /// <summary>
    /// The install directive. Null leaves the type default in force.
    /// </summary>
    public InstallInfo? Install { get; }

    /// <summary>
    /// The loader bounds current at stamp time. Null when the mod runs without one.
    /// </summary>
    public LoaderRequirement? Loader { get; }

    /// <summary>
    /// The merged dependency list, each entry marked authored or derived.
    /// </summary>
    public IReadOnlyList<ModDependency> Dependencies { get; }

    /// <summary>
    /// URL or text of the release's changelog, if any.
    /// </summary>
    public string? Changelog { get; }

    /// <summary>
    /// The listing facts as they stood at stamp time, for release-accurate display.
    /// </summary>
    public ListingSnapshot? Listing { get; }

    /// <summary>
    /// True when the author retracted this release. Not offered for installs or updates.
    /// </summary>
    public bool Yanked { get; }

    /// <summary>
    /// Optional free text shown alongside the yank warning.
    /// </summary>
    public string? YankedReason { get; }

    public ModVersionMetadata(
        int specVersion,
        string modId,
        ModVersion version,
        ReleaseStatus releaseStatus,
        DateTimeOffset releaseDate,
        string gameMin,
        int gameMinRevision,
        DownloadInfo download,
        long? installSizeBytes,
        IReadOnlyList<ModDependency> dependencies,
        ContentType type = ContentType.Mod,
        string versionScheme = "semver",
        string? gameMax = null,
        int? gameMaxRevision = null,
        IReadOnlyList<string>? os = null,
        InstallInfo? install = null,
        LoaderRequirement? loader = null,
        string? changelog = null,
        ListingSnapshot? listing = null,
        bool yanked = false,
        string? yankedReason = null,
        string? source = null)
    {
        if (specVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(specVersion), "Spec version must be a positive integer.");

        ModIds.Validate(modId, nameof(modId));

        if (type == ContentType.ModPack)
            throw new ArgumentException("Packs have no generated release files.", nameof(type));

        if (loader is not null && type != ContentType.Mod)
            throw new ArgumentException("Only a mod can declare a loader requirement.", nameof(loader));

        if (string.IsNullOrWhiteSpace(versionScheme))
            throw new ArgumentException("Version scheme cannot be null or whitespace.", nameof(versionScheme));

        if (string.IsNullOrWhiteSpace(gameMin))
            throw new ArgumentException("The minimum game version is required.", nameof(gameMin));

        if (gameMinRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(gameMinRevision), "The minimum game revision cannot be negative.");

        if (GameVersion.TryParse(gameMin, out var minParsed) && minParsed.Revision != gameMinRevision)
            throw new ArgumentException("The displayed minimum game version and its revision disagree.", nameof(gameMinRevision));

        if ((gameMax is null) != (gameMaxRevision is null))
            throw new ArgumentException("The maximum game version and its revision must both be present or both absent.", nameof(gameMaxRevision));

        if (gameMaxRevision is { } maxRevision)
        {
            if (maxRevision < gameMinRevision)
                throw new ArgumentOutOfRangeException(nameof(gameMaxRevision), "The maximum game revision cannot be below the minimum.");

            if (GameVersion.TryParse(gameMax, out var maxParsed) && maxParsed.Revision != maxRevision)
                throw new ArgumentException("The displayed maximum game version and its revision disagree.", nameof(gameMaxRevision));
        }

        if (download is null)
            throw new ArgumentNullException(nameof(download));

        if (dependencies is null)
            throw new ArgumentNullException(nameof(dependencies));

        if (installSizeBytes is < 0)
            throw new ArgumentOutOfRangeException(nameof(installSizeBytes), "Install size cannot be negative.");

        SpecVersion = specVersion;
        ModId = modId;
        Type = type;
        Version = version;
        VersionScheme = versionScheme;
        ReleaseStatus = releaseStatus;
        ReleaseDate = releaseDate;
        GameMin = gameMin;
        GameMinRevision = gameMinRevision;
        GameMax = gameMax;
        GameMaxRevision = gameMaxRevision;
        Os = os is null ? null : new ReadOnlyCollection<string>(os.ToArray());
        Download = download;
        InstallSizeBytes = installSizeBytes;
        Install = install;
        Loader = loader;
        Dependencies = new ReadOnlyCollection<ModDependency>(dependencies.ToArray());
        Changelog = changelog;
        Listing = listing;
        Yanked = yanked;
        YankedReason = yankedReason;
        Source = source;
    }
}
