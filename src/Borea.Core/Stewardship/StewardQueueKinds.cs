using System.Text.RegularExpressions;
using Borea.Core.Listings;

namespace Borea.Core.Stewardship;

/// <summary>
/// What a pull request of an index repository changes. The document kinds are the labels that the checks set, as tools/decide.py
/// of each repository writes them, and the steward files of content-index come from the changed paths.
/// </summary>
public static partial class StewardQueueKinds
{
    public const string NeedsStewardLabel = "needs-steward";

    private static readonly Dictionary<string, StewardQueueKind> ContentIndexLabels = new(StringComparer.Ordinal)
    {
        ["listing"] = StewardQueueKind.Listing,
        ["pack"] = StewardQueueKind.Pack,
    };

    private static readonly Dictionary<string, StewardQueueKind> ReleasesLabels = new(StringComparer.Ordinal)
    {
        ["release"] = StewardQueueKind.Release,
        ["amendment"] = StewardQueueKind.Amendment,
    };

    /// <summary>
    /// The owner records of content-index that a pull request adds together with a version of their own pack, as claims in
    /// tools/check_scope.py of content-index finds them. Such a first pack claim merges itself (RFC 0080), so only a change of an
    /// accepted owner record, such as a handover, is a steward file.
    /// </summary>
    /// <param name="files">Every changed file of a pull request of content-index.</param>
    public static IReadOnlySet<string> FirstClaims(IEnumerable<StewardQueueFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var added = files.Where(file => file.Status == "added").Select(file => file.Path).ToList();
        var folders = added.Where(path => PackDocument().IsMatch(path)).Select(FolderOf).ToHashSet(StringComparer.Ordinal);
        return added.Where(path => OwnerRecord().IsMatch(path) && folders.Contains(FolderOf(path))).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether the path is packs/&lt;id&gt;/owner.json of content-index.</summary>
    public static bool IsOwnerRecord(string repository, string? path) =>
        string.Equals(repository, ListingPullRequestLinks.Repository, StringComparison.OrdinalIgnoreCase) && path is not null && OwnerRecord().IsMatch(path);

    private static string FolderOf(string path) => path[..path.LastIndexOf('/')];

    /// <param name="labels">The label names of the pull request.</param>
    /// <param name="files">Every changed file with the status GitHub gives it.</param>
    /// <returns>The kinds in the order of <see cref="StewardQueueKind"/>, and whether a file is none of them.</returns>
    /// <exception cref="ArgumentException">The repository is no index repository.</exception>
    public static (IReadOnlyList<StewardQueueKind> Kinds, bool HasOtherFiles) Of(string repository, IEnumerable<string> labels, IEnumerable<StewardQueueFile> files)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(files);
        var isContentIndex = string.Equals(repository, ListingPullRequestLinks.Repository, StringComparison.OrdinalIgnoreCase);
        if (!isContentIndex && !string.Equals(repository, StewardAccess.ReleasesRepository, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{repository} is no index repository.", nameof(repository));

        var byLabel = isContentIndex ? ContentIndexLabels : ReleasesLabels;
        var kinds = new HashSet<StewardQueueKind>(labels.Where(byLabel.ContainsKey).Select(label => byLabel[label]));
        var hasOtherFiles = false;
        var changed = files.ToList();
        IReadOnlySet<string> claims = isContentIndex ? FirstClaims(changed) : new HashSet<string>();
        foreach (var file in changed)
        {
            // the owner record of a first claim is part of its pack document, whose kind comes from its label
            if (claims.Contains(file.Path))
                continue;

            // the checks take a document only when it is added or changed, so a removed or renamed one goes to a steward on GitHub
            var writes = file.Status is "added" or "modified";
            foreach (var path in new[] { file.Path, file.PreviousPath }.OfType<string>())
            {
                if (!IsKnown(isContentIndex, path, writes, out var kind))
                    hasOtherFiles = true;
                else if (kind is { } found)
                    kinds.Add(found);
            }
        }

        return (kinds.Order().ToList(), hasOtherFiles);
    }

    /// <summary>Listing or Pack for a document of content-index that the pull request adds or changes, and null for any other file.</summary>
    /// <param name="status">The status that GitHub gives the file.</param>
    public static StewardQueueKind? DocumentOf(string repository, string path, string status)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!string.Equals(repository, ListingPullRequestLinks.Repository, StringComparison.OrdinalIgnoreCase) || status is not ("added" or "modified"))
            return null;

        return ListingDocument().IsMatch(path) ? StewardQueueKind.Listing
            : PackDocument().IsMatch(path) ? StewardQueueKind.Pack
            : null;
    }

    /// <summary>
    /// Whether the path is a steward file or a written document of the repository. The kind is null for a document, whose kind
    /// comes from its label.
    /// </summary>
    private static bool IsKnown(bool isContentIndex, string path, bool writes, out StewardQueueKind? kind)
    {
        kind = !isContentIndex ? null : path switch
        {
            IndexStatusDocument.Path => StewardQueueKind.IndexStatus,
            "tags.toml" => StewardQueueKind.TagVocabulary,
            _ when OwnerRecord().IsMatch(path) => StewardQueueKind.OwnerRecord,
            _ => null,
        };
        return kind is not null || (writes && (isContentIndex ? ListingDocument().IsMatch(path) || PackDocument().IsMatch(path) : ReleaseFile().IsMatch(path)));
    }

    [GeneratedRegex(@"\Alistings/[^/]+\.[Tt][Oo][Mm][Ll]\z")]
    private static partial Regex ListingDocument();

    [GeneratedRegex(@"\Apacks/[^/]+/[^/]+\.[Tt][Oo][Mm][Ll]\z")]
    private static partial Regex PackDocument();

    [GeneratedRegex(@"\Apacks/[^/]+/owner\.json\z")]
    private static partial Regex OwnerRecord();

    [GeneratedRegex(@"\Areleases/[A-Za-z0-9][^/]*/[^/]+\.[Jj][Ss][Oo][Nn]\z")]
    private static partial Regex ReleaseFile();
}

/// <param name="Path">The path of the file after the change.</param>
/// <param name="Status">The status that GitHub gives the file, such as added, modified, removed or renamed.</param>
/// <param name="PreviousPath">The old path of a renamed file, or null.</param>
public sealed record StewardQueueFile(string Path, string Status, string? PreviousPath = null);
