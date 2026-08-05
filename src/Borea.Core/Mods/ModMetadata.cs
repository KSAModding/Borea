using Borea.Core.Dependencies;
using Borea.Core.Game;
using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// A snapshot of a mod's descriptive and release information, as of a
/// specific version.
/// </summary>
public sealed class ModMetadata
{
    public string ModId { get; }
    public string Source { get; }
    public string Name { get; }
    public string Author { get; }
    public ModVersion? Version { get; }
    public GameVersion? BuiltForGameVersion { get; }
    public string Description { get; }
    public string? HomepageUrl { get; }
    public string? ChangeLog { get; }
    public DateTimeOffset? ReleasedAt { get; }
    public long? FileSizeBytes { get; }
    public IReadOnlyList<ModDependency> Dependencies { get; }
    public IReadOnlyList<string> Tags { get; }

    public ModMetadata(
        string modId,
        string source,
        string name,
        string author,
        string description,
        ModVersion? version = null,
        GameVersion? builtForGameVersion = null,
        DateTimeOffset? releasedAt = null,
        long? fileSizeBytes = null,
        IReadOnlyList<ModDependency>? dependencies = null,
        IReadOnlyList<string>? tags = null,
        string? homepageUrl = null,
        string? changeLog = null)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be null or whitespace.", nameof(source));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));

        if (string.IsNullOrWhiteSpace(author))
            throw new ArgumentException("Author cannot be null or whitespace.", nameof(author));

        if (description is null)
            throw new ArgumentNullException(nameof(description));

        ModId = modId;
        Source = source;
        Name = name;
        Author = author;
        Version = version;
        BuiltForGameVersion = builtForGameVersion;
        Description = description;
        ReleasedAt = releasedAt;
        FileSizeBytes = fileSizeBytes;
        Dependencies = dependencies is null ? Array.Empty<ModDependency>() : new ReadOnlyCollection<ModDependency>(dependencies.ToArray());
        Tags = tags is null ? Array.Empty<string>() : new ReadOnlyCollection<string>(tags.ToArray());
        HomepageUrl = homepageUrl;
        ChangeLog = changeLog;
    }
}
