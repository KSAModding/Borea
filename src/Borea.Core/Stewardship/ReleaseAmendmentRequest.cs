using System.Text;
using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>A steward amendment of the releases of one listing, as the steward asks for it. It goes into one pull request of content-index-releases.</summary>
/// <param name="ListingId">The id of the listing, as its release folder spells it.</param>
/// <param name="Change">The change as typed. A yank without a reason of its own takes <paramref name="Reason"/>.</param>
/// <param name="Reason">One sentence for the pull request, and for a yank also the reason that players see.</param>
/// <param name="AuthorRequest">
/// The https link to the request of the author on whose behalf the steward amends, or null for the steward's own amendment.
/// With it the amendment may also widen, as the owner may (RFC 0079), and the pull request names it.
/// </param>
public sealed record ReleaseAmendmentRequest(string ListingId, ReleaseSelection Selection, ReleaseChange Change, string Reason, string? AuthorRequest = null)
{
    /// <summary>The line of the pull request description that names the author's request, as REQUEST of tools/check_amendment.py spells it.</summary>
    public const string RequestLine = "Requested by the author:";

    /// <summary>The characters that a value of <see cref="Command"/> keeps without quotes.</summary>
    private const string PlainPunctuation = "._-:=/@+";

    /// <summary>A steward on the author's request widens as the owner does, and a steward alone only narrows.</summary>
    public ReleaseAmender Amender => AuthorRequest is null ? ReleaseAmender.Steward : ReleaseAmender.Owner;

    /// <summary>The change that goes into the files.</summary>
    public ReleaseChange Amendment => Change is { Yank: true, YankReason: null } ? Change with { YankReason = Reason.Trim() } : Change;

    /// <summary>The branch of content-index-releases the amendment goes to, steward/amend-&lt;id&gt;.</summary>
    public string Branch => $"{IndexStatusChange.BranchPrefix}amend-{ListingId.ToLowerInvariant()}";

    /// <summary>
    /// Whether it changes loader bounds, os or dependency entries that the listing in content-index also states, which the next stamp
    /// takes from the listing.
    /// </summary>
    public bool ChangesListingFields =>
        Change.LoaderMin is not null || Change.LoaderMax is not null || Change.RemoveLoaderMin || Change.RemoveLoaderMax || Change.Os is not null
        || Change.AddedDependencies.Count > 0 || Change.DependencyBounds.Count > 0 || Change.RemovedDependencyBounds.Count > 0 || Change.DependencyKinds.Count > 0
        || Change.RemovedDependencies.Count > 0;

    /// <summary>Whether the change names anything that an option of tools/amend.py expresses.</summary>
    public bool HasToolOptions => Amendment is var change
        && (change.GameMin is not null || change.GameMax is not null || change.Yank || change.YankReason is not null || change.LoaderMin is not null
            || change.LoaderMax is not null || change.AddedDependencies.Count > 0 || change.DependencyBounds.Count > 0);

    /// <summary>The changes that tools/amend.py has no option for, in words, which <see cref="Command"/> leaves out. Only the owner makes them.</summary>
    public IReadOnlyList<string> ChangesWithoutToolOption
    {
        get
        {
            var change = Amendment;
            var changes = new List<string>();
            if (change.RemoveGameMax)
                changes.Add("`game_max` is removed");
            if (change.Os is { } os)
                changes.Add(os.Count == 0 ? "`os` is removed" : $"`os` becomes `{string.Join(", ", os.Select(platform => platform.Trim()))}`");
            if (change.Unyank)
                changes.Add("the yank is taken back");
            if (change.RemoveLoaderMin)
                changes.Add("the loader min is removed");
            if (change.RemoveLoaderMax)
                changes.Add("the loader max is removed");
            foreach (var removal in change.RemovedDependencyBounds)
            {
                var bounds = (removal.Min, removal.Max) switch
                {
                    (true, true) => "the min and the max",
                    (true, false) => "the min",
                    _ => "the max",
                };
                changes.Add($"{bounds} of `{removal.Id.Trim()}` is removed");
            }

            changes.AddRange(change.DependencyKinds.Select(retyped => $"`{retyped.Id.Trim()}` becomes `{retyped.Kind.Trim()}`"));
            changes.AddRange(change.RemovedDependencies.Select(id => $"the dependency `{id.Trim()}` is removed"));
            return changes;
        }
    }

    /// <summary>
    /// Whether the link can stand in the request line, where the checks of content-index-releases find it: one absolute https link
    /// without white space or angle brackets.
    /// </summary>
    public static bool IsValidAuthorRequest(string? link) =>
        link is { Length: > 0 }
        && link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && !link.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '<' or '>')
        && Uri.TryCreate(link, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Length > 0;

    /// <summary>The same amendment as a command of tools/amend.py for a POSIX shell, so a reviewer can derive it again.</summary>
    public string Command
    {
        get
        {
            var arguments = new List<string> { "python3", "tools/amend.py", "--listing", ListingId };
            if (Selection.UpToVersion is { } upTo)
                arguments.AddRange(["--up-to", upTo.Trim()]);
            else if (Selection.Versions is { } versions)
                arguments.AddRange(versions.SelectMany(version => new[] { "--version", version.Trim() }));
            else
                arguments.Add("--all");
            if (Amender == ReleaseAmender.Owner)
                arguments.Add("--owner");

            var change = Amendment;
            void Option(string name, string? value)
            {
                if (value is not null)
                    arguments.AddRange([name, value.Trim()]);
            }

            Option("--game-min", change.GameMin);
            Option("--game-max", change.GameMax);
            if (change.Yank)
                arguments.Add("--yank");
            Option("--reason", change.YankReason);
            Option("--loader-min", change.LoaderMin);
            Option("--loader-max", change.LoaderMax);
            foreach (var bounds in change.DependencyBounds.Where(bounds => bounds.Min is not null))
                Option("--dependency-min", $"{bounds.Id.Trim()}={bounds.Min!.Trim()}");
            foreach (var bounds in change.DependencyBounds.Where(bounds => bounds.Max is not null))
                Option("--dependency-max", $"{bounds.Id.Trim()}={bounds.Max!.Trim()}");
            foreach (var addition in change.AddedDependencies)
                Option("--add-dependency", $"{addition.Id.Trim()}:{addition.Kind.Trim()}");

            return string.Join(' ', arguments.Select(Quote));
        }
    }

    /// <summary>A value as a POSIX shell reads it back, in single quotes unless it is plain.</summary>
    private static string Quote(string value) =>
        value.Length > 0 && value.All(character => char.IsAsciiLetterOrDigit(character) || PlainPunctuation.Contains(character))
            ? value
            : "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}

/// <summary>An amendment derived at one commit of the base branch, before anything is written.</summary>
/// <param name="Files">Every selected release file, in the order of the selection.</param>
/// <param name="Owners">The logins that own the listing, as the ownership proof names them, without the signed-in steward.</param>
public sealed record ReleaseAmendmentPreview(ReleaseAmendmentRequest Request, IReadOnlyList<ReleaseFilePreview> Files, IReadOnlyList<string> Owners)
{
    /// <summary>The title names at most this many versions, and a range beyond.</summary>
    private const int TitleVersions = 3;

    /// <summary>The files that the amendment changes.</summary>
    public IReadOnlyList<ReleaseFilePreview> Changed => [.. Files.Where(file => file.After is not null)];

    /// <summary>The title of the pull request and the commit message, such as "Amend MyMod 1.0.0, 1.1.0".</summary>
    public string Title
    {
        get
        {
            var versions = ChangedVersions();
            return versions.Count switch
            {
                0 => $"Amend {Request.ListingId}",
                <= TitleVersions => $"Amend {Request.ListingId} {string.Join(", ", versions)}",
                _ => $"Amend {Request.ListingId} {versions[0]} to {versions[^1]} ({versions.Count} releases)",
            };
        }
    }

    /// <summary>The body of the pull request. It mentions each owner, so the owner is told.</summary>
    public string Body
    {
        get
        {
            var versions = ChangedVersions();
            var body = new StringBuilder(versions.Count == 1
                ? $"Amends release {versions[0]} of `{Request.ListingId}`."
                : $"Amends {versions.Count} releases of `{Request.ListingId}`: {string.Join(", ", versions)}.");
            body.Append("\n\nReason: ").Append(Request.Reason.Trim());
            if (Request.AuthorRequest is { } link)
                body.Append("\n\n").Append(ReleaseAmendmentRequest.RequestLine).Append(" <").Append(link).Append('>');
            if (Request.HasToolOptions)
                body.Append("\n\nThe same amendment with the tools of this repository:\n\n```text\n").Append(Request.Command).Append("\n```");
            if (Request.ChangesWithoutToolOption is { Count: > 0 } changes)
            {
                body.Append(Request.HasToolOptions
                        ? "\n\ntools/amend.py has no option for some of these changes, so the command leaves out: "
                        : "\n\ntools/amend.py has no option for these changes, so this amendment has no command: ")
                    .Append(string.Join("; ", changes)).Append('.');
            }

            if (Request.ChangesListingFields)
                body.Append("\n\nThe listing in content-index states its bounds, os and dependencies separately, so the next release is stamped without this change until the listing has it too.");
            if (OwnerMention.Of(Owners, Request.ListingId) is { } mention)
                body.Append("\n\n").Append(mention);

            return body.ToString();
        }
    }

    /// <summary>Whether both derive the same files from the same texts, so what the steward saw is what is sent.</summary>
    public bool HasSameFiles(ReleaseAmendmentPreview other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Files.SequenceEqual(other.Files);
    }

    /// <summary>The changed versions, oldest first by SemVer precedence.</summary>
    private List<string> ChangedVersions() =>
        [.. Changed.Select(file => file.Version).OrderBy(ModVersion.Parse).ThenBy(version => version, StringComparer.Ordinal)];
}

/// <param name="Path">The path of the file in content-index-releases.</param>
/// <param name="Before">The text of the file on the base branch.</param>
/// <param name="After">The amended text, or null when the release already says this.</param>
public sealed record ReleaseFilePreview(string Version, string Path, string Before, string? After)
{
    /// <summary>The change as a unified diff without file headers, the form GitHub shows, or null when nothing changes.</summary>
    public string? Patch => After is null ? null : LineDiff.Unified(Before, After);
}
