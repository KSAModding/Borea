using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using System.Collections.ObjectModel;
using System.Text;

namespace Borea.Core.Mods;

/// <summary>
/// One stamped release of a mod or mod loader, mirroring the generated release file of RFC 0031.
/// </summary>
public sealed class ModVersionMetadata
{
    public const int MaxChangelogTextBytes = 16 * 1024;

    public int SpecVersion { get; }

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
    /// The archive that Borea takes, where it is and what it hashes to. It is <see cref="ReleaseDownload"/> unless
    /// <see cref="ForPlatform"/> or <see cref="ForArchive"/> took an archive of <see cref="PlatformDownloads"/>.
    /// </summary>
    public DownloadInfo Download { get; }

    /// <summary>
    /// The download of the release file, the archive for every platform. The watcher marks the release as gone
    /// (RFC 0078) only by this archive.
    /// </summary>
    public DownloadInfo ReleaseDownload { get; }

    /// <summary>
    /// The smaller archive of each platform that the author built one for, as the release file stamps them. Empty when
    /// the release has none.
    /// </summary>
    public IReadOnlyDictionary<OsPlatform, PlatformDownload> PlatformDownloads { get; }

    /// <summary>
    /// Which source this release came from. Borea-internal, not a format field.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Unpacked size of <see cref="Download"/> in bytes. Null when the source does not expose it.
    /// </summary>
    public long? InstallSizeBytes { get; }

    private readonly long? _releaseInstallSizeBytes;

    /// <summary>
    /// The install directive. Null leaves the type default in force.
    /// </summary>
    public InstallInfo? Install { get; }

    /// <summary>
    /// The paths the listing's [install].manages claims (RFC 0035), carried
    /// into the release because the release file does not stamp them. Null
    /// means the author said nothing.
    /// </summary>
    public IReadOnlyList<string>? Manages { get; }

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
    /// The release notes as CommonMark, as the index copied them at stamp time.
    /// </summary>
    public string? ChangelogText { get; }

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

    /// <summary>
    /// When the watcher found the forums thread of the listing of this release gone (RFC 0110), as the index snapshot says.
    /// Null when the thread is fine. The record of an installed copy does not store it, because the mark belongs to the listing and not to the copy.
    /// </summary>
    public DateTimeOffset? ForumsUnavailableSince { get; }

    /// <summary>
    /// Whether Borea may pick this release for an install or an update by itself: it is not yanked,
    /// its download is not gone from its host (RFC 0078), and the forums thread of its listing is not gone (RFC 0110). An exact request can still name it.
    /// </summary>
    public bool IsOffered => !Yanked && Download.UnavailableSince is null && ForumsUnavailableSince is null;

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
        string? changelogText = null,
        ListingSnapshot? listing = null,
        bool yanked = false,
        string? yankedReason = null,
        string? source = null,
        IReadOnlyList<string>? manages = null,
        IReadOnlyDictionary<OsPlatform, PlatformDownload>? platformDownloads = null,
        DateTimeOffset? forumsUnavailableSince = null)
    {
        if (specVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(specVersion), "Spec version must be a positive integer.");

        ModIds.Validate(modId, nameof(modId));

        if (type == ContentType.ModPack)
            throw new ArgumentException("Packs have no generated release files.", nameof(type));

        if (loader is not null && type != ContentType.Mod)
            throw new ArgumentException("Only a mod can declare a loader requirement.", nameof(loader));

        if (type.IsVehicleOrSave())
        {
            // The game shows the item by this name and writes it back into a folder of this name, so only a name it keeps works.
            if (!GameSaveName.IsValid(install?.Folder))
                throw new ArgumentException($"A vehicle or save needs an install folder that the game keeps as it is, not '{install?.Folder}'.", nameof(install));

            if (install!.Target is not null || install.Path is not null)
                throw new ArgumentException("A vehicle or save has no install target or path, because the type default places it.", nameof(install));

            if (manages is not null)
                throw new ArgumentException("A vehicle or save manages no paths.", nameof(manages));
        }

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

        if (platformDownloads is { Count: > 0 })
        {
            if (type is not (ContentType.Mod or ContentType.ModLoader))
                throw new ArgumentException("Only a mod or a mod loader has platform downloads.", nameof(platformDownloads));

            if (platformDownloads.Values.Any(entry => entry is null || entry.InstallSizeBytes < 0))
                throw new ArgumentException("A platform download needs a download and an install size that is not negative.", nameof(platformDownloads));
        }

        if (changelogText is not null && Encoding.UTF8.GetByteCount(changelogText) > MaxChangelogTextBytes)
            throw new ArgumentException($"The changelog text can have at most {MaxChangelogTextBytes} bytes of UTF-8.", nameof(changelogText));

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
        ReleaseDownload = download;
        PlatformDownloads = new ReadOnlyDictionary<OsPlatform, PlatformDownload>(platformDownloads is null ? new Dictionary<OsPlatform, PlatformDownload>() : new Dictionary<OsPlatform, PlatformDownload>(platformDownloads));
        InstallSizeBytes = installSizeBytes;
        _releaseInstallSizeBytes = installSizeBytes;
        Install = install;
        Manages = manages is null ? null : new ReadOnlyCollection<string>(manages.ToArray());
        Loader = loader;
        Dependencies = new ReadOnlyCollection<ModDependency>(dependencies.ToArray());
        Changelog = changelog;
        ChangelogText = changelogText;
        Listing = listing;
        Yanked = yanked;
        YankedReason = yankedReason;
        Source = source;
        ForumsUnavailableSince = forumsUnavailableSince;
    }

    /// <summary>The same release with <paramref name="download"/> as the archive that Borea takes.</summary>
    private ModVersionMetadata(ModVersionMetadata release, DownloadInfo download, long? installSizeBytes)
    {
        SpecVersion = release.SpecVersion;
        ModId = release.ModId;
        Type = release.Type;
        Version = release.Version;
        VersionScheme = release.VersionScheme;
        ReleaseStatus = release.ReleaseStatus;
        ReleaseDate = release.ReleaseDate;
        GameMin = release.GameMin;
        GameMinRevision = release.GameMinRevision;
        GameMax = release.GameMax;
        GameMaxRevision = release.GameMaxRevision;
        Os = release.Os;
        Download = download;
        ReleaseDownload = release.ReleaseDownload;
        PlatformDownloads = release.PlatformDownloads;
        InstallSizeBytes = installSizeBytes;
        _releaseInstallSizeBytes = release._releaseInstallSizeBytes;
        Install = release.Install;
        Manages = release.Manages;
        Loader = release.Loader;
        Dependencies = release.Dependencies;
        Changelog = release.Changelog;
        ChangelogText = release.ChangelogText;
        Listing = release.Listing;
        Yanked = release.Yanked;
        YankedReason = release.YankedReason;
        Source = release.Source;
        ForumsUnavailableSince = release.ForumsUnavailableSince;
    }

    /// <summary>
    /// The release as Borea takes it for a game on <paramref name="platform"/>, which is the archive of that platform
    /// while it is not gone from its host, or else the archive for every platform. The installers, the downloader, the size
    /// shown and the space check then all read the taken archive from <see cref="Download"/>.
    /// </summary>
    public ModVersionMetadata ForPlatform(OsPlatform platform) =>
        PlatformDownloads.TryGetValue(platform, out var entry) && entry.Download.UnavailableSince is null
            ? Taking(entry.Download, entry.InstallSizeBytes)
            : Taking(ReleaseDownload, _releaseInstallSizeBytes);

    /// <summary>
    /// The release taking the one of its archives whose bytes have the hex digest <paramref name="sha256"/>, so that
    /// a copy unpacked from the archive for every platform or from a platform archive both count. Null when no
    /// archive of the release matches.
    /// </summary>
    public ModVersionMetadata? ForArchive(string sha256)
    {
        if (ReleaseDownload.HashMatches(sha256))
            return Taking(ReleaseDownload, _releaseInstallSizeBytes);

        var entry = PlatformDownloads.Values.FirstOrDefault(candidate => candidate.Download.HashMatches(sha256));
        return entry is null ? null : Taking(entry.Download, entry.InstallSizeBytes);
    }

    private ModVersionMetadata Taking(DownloadInfo download, long? installSizeBytes) =>
        ReferenceEquals(download, Download) ? this : new ModVersionMetadata(this, download, installSizeBytes);
}
