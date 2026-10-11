using System.Text;
using Borea.Core.Listings;
using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>
/// The takedown requests and id disputes of content-index, which people file with the issue forms of that repository.
/// <see cref="IReportActions"/> answers, closes and reopens them.
/// </summary>
public interface IIndexReports
{
    /// <summary>Reads the reports without the token, because the issues are public.</summary>
    /// <exception cref="StewardException">GitHub could not be read.</exception>
    Task<IReadOnlyList<IndexReport>> ListAsync(IndexReportFilter filter = IndexReportFilter.Open, CancellationToken cancellationToken = default);
}

public enum IndexReportFilter
{
    /// <summary>The open reports, oldest first.</summary>
    Open,

    /// <summary>
    /// The 100 closed reports that changed last, found by the tag in the title, the last changed first.
    /// A new comment changes an issue, so a closed report with new evidence comes first.
    /// </summary>
    RecentlyClosed,
}

/// <summary>Why a report was closed, as GitHub names it.</summary>
public enum IndexReportCloseReason
{
    Completed,
    NotPlanned,
}

public enum IndexReportKind
{
    Takedown,
    Dispute,
}

/// <summary>
/// One report with the sections of its form. GitHub writes each field of an issue form into the body as a heading with the label,
/// followed by the answer, and "_No response_" for an optional field left empty. A body that was edited by hand can lack a section,
/// which is then null, and a section that the form requires is named in <see cref="Missing"/>.
/// </summary>
/// <param name="Author">The login that filed the report, or null when GitHub names none.</param>
public sealed record IndexReport(int Number, Uri Url, string Title, string? Author, DateTimeOffset Created, IndexReportKind Kind)
{
    public const string Repository = ListingPullRequestLinks.Repository;

    public const string TakedownPrefix = "[Takedown]";

    public const string DisputePrefix = "[Dispute]";

    public const string TakedownForm = "takedown.yml";

    public const string DisputeForm = "id-dispute.yml";

    // The id of the listing id field in both forms. GitHub fills a field of an issue form from the query parameter with its id.
    private const string ListingField = "listing";

    // The labels of the fields in .github/ISSUE_TEMPLATE/takedown.yml and id-dispute.yml of content-index.
    public const string ListingLabel = "Listing id";

    public const string GroundLabel = "Ground";

    public const string WhatIsWrongLabel = "What is wrong";

    public const string ReporterLabel = "Who you are";

    public const string DisputedLabel = "What is disputed";

    public const string ForumsLabel = "Your forums thread";

    public const string ClaimLabel = "Your claim";

    public const string OtherPartyLabel = "The other party";

    private const string NoResponse = "_No response_";

    private static readonly string[] TakedownLabels = [ListingLabel, GroundLabel, WhatIsWrongLabel, ReporterLabel];

    private static readonly string[] DisputeLabels = [ListingLabel, DisputedLabel, ForumsLabel, ClaimLabel, OtherPartyLabel];

    public bool IsClosed { get; init; }

    /// <summary>Why the report was closed, or null when it is open or GitHub names another reason, such as a duplicate.</summary>
    public IndexReportCloseReason? CloseReason { get; init; }

    /// <summary>When the report was closed, or null when it is open.</summary>
    public DateTimeOffset? Closed { get; init; }

    /// <summary>The listing id field as the reporter typed it.</summary>
    public string? ListingText { get; init; }

    /// <summary>The listing or pack id that the listing id field names, or null when Borea cannot read one there.</summary>
    public string? Id { get; init; }

    /// <summary>The pack version that the listing id field names after the id, or null.</summary>
    public string? Version { get; init; }

    /// <summary>The ground of a takedown, as the reporter chose it in the form.</summary>
    public string? Ground { get; init; }

    public string? WhatIsWrong { get; init; }

    /// <summary>What the reporter of a takedown says about their relation to the content.</summary>
    public string? Reporter { get; init; }

    /// <summary>What a dispute contests, as the reporter chose it in the form.</summary>
    public string? Disputed { get; init; }

    public string? ForumsThread { get; init; }

    public string? Claim { get; init; }

    public string? OtherParty { get; init; }

    /// <summary>The labels of the sections that the form requires and the body does not have.</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>
    /// The forums thread of a dispute when it is a thread link of forums.ahwoo.com, or null. A link to another host, or one with
    /// more text after it, only shows as it was typed, because the button says that it opens the forums thread.
    /// </summary>
    public Uri? ForumsUrl =>
        ForumsThreadLink.ThreadOf(ForumsThread) is not null && Uri.TryCreate(ForumsThread, UriKind.Absolute, out var url) ? url : null;

    /// <summary>
    /// Whether the title starts with the tag of a report form. The tag compares ignoring case and the space after it,
    /// so a reporter who changed them still shows up.
    /// </summary>
    public static IndexReportKind? KindOf(string? title)
    {
        var trimmed = title?.TrimStart() ?? string.Empty;
        return trimmed.StartsWith(TakedownPrefix, StringComparison.OrdinalIgnoreCase) ? IndexReportKind.Takedown
            : trimmed.StartsWith(DisputePrefix, StringComparison.OrdinalIgnoreCase) ? IndexReportKind.Dispute
            : null;
    }

    /// <summary>The report that an issue holds, or null when its title is no report.</summary>
    public static IndexReport? FromIssue(int number, Uri url, string title, string? author, DateTimeOffset created, string? body)
    {
        if (KindOf(title) is not { } kind)
            return null;

        var labels = kind == IndexReportKind.Takedown ? TakedownLabels : DisputeLabels;
        var sections = SectionsOf(body, labels);
        var listing = sections.GetValueOrDefault(ListingLabel);
        var (id, version) = IdOf(listing);
        return new IndexReport(number, url, title, author, created, kind)
        {
            ListingText = listing,
            Id = id,
            Version = version,
            Ground = sections.GetValueOrDefault(GroundLabel),
            WhatIsWrong = sections.GetValueOrDefault(WhatIsWrongLabel),
            Reporter = sections.GetValueOrDefault(ReporterLabel),
            Disputed = sections.GetValueOrDefault(DisputedLabel),
            ForumsThread = sections.GetValueOrDefault(ForumsLabel),
            Claim = sections.GetValueOrDefault(ClaimLabel),
            OtherParty = sections.GetValueOrDefault(OtherPartyLabel),
            Missing = labels.Where(label => label != OtherPartyLabel && sections.GetValueOrDefault(label) is null).ToList(),
        };
    }

    /// <summary>
    /// The id, and the version after it, that the listing id field names: "MyMod", "my-pack 1.2.0" or "my-pack@v1.2.0".
    /// Any other text names no id, so that no action takes a word of a sentence as the id.
    /// </summary>
    public static (string? Id, string? Version) IdOf(string? text)
    {
        var parts = (text ?? string.Empty).Replace('`', ' ').Replace('@', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2 || !ModIds.IsValid(parts[0]))
            return (null, null);
        if (parts.Length == 1)
            return (parts[0], null);

        var version = parts[1].Length > 1 && parts[1][0] is 'v' or 'V' && char.IsAsciiDigit(parts[1][1]) ? parts[1][1..] : parts[1];
        return ModVersion.TryParse(version, out _) ? (parts[0], version) : (null, null);
    }

    /// <summary>
    /// The new issue page of the form for <paramref name="kind"/>, with the listing id field filled in as <see cref="IdOf"/> reads it.
    /// Only a takedown names a pack version, because a dispute is about the id itself.
    /// </summary>
    public static Uri FormUrl(IndexReportKind kind, string id, ModVersion? version = null)
    {
        if (!ModIds.IsValid(id))
            throw new ArgumentException($"'{id}' is not a listing id.", nameof(id));
        if (version is not null && kind != IndexReportKind.Takedown)
            throw new ArgumentException("Only a takedown names a pack version.", nameof(version));

        var form = kind == IndexReportKind.Takedown ? TakedownForm : DisputeForm;
        var listing = version is { } named ? $"{id} {named}" : id;
        return new Uri($"https://github.com/{Repository}/issues/new?template={form}&{ListingField}={Uri.EscapeDataString(listing)}");
    }

    /// <summary>
    /// The answer under each heading of <paramref name="labels"/>, or null for "_No response_" and an empty one. The first heading of a label
    /// starts its section, so a later line that repeats it, for example inside the text of a reporter, stays text of the section it is in.
    /// </summary>
    private static Dictionary<string, string?> SectionsOf(string? body, IReadOnlyCollection<string> labels)
    {
        var sections = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? current = null;
        var text = new StringBuilder();
        foreach (var line in (body ?? string.Empty).ReplaceLineEndings("\n").Split('\n'))
        {
            if (LabelOf(line, labels) is { } label && label != current && !sections.ContainsKey(label))
            {
                Close();
                current = label;
                continue;
            }

            if (current is not null)
                text.Append(line).Append('\n');
        }

        Close();
        return sections;

        void Close()
        {
            if (current is not null)
            {
                var value = text.ToString().Trim();
                sections[current] = value.Length == 0 || value == NoResponse ? null : value;
            }

            text.Clear();
        }
    }

    /// <summary>The label of a markdown heading line such as "### Ground", or null.</summary>
    private static string? LabelOf(string line, IReadOnlyCollection<string> labels)
    {
        var trimmed = line.Trim();
        var hashes = trimmed.Length - trimmed.TrimStart('#').Length;
        if (hashes is 0 or > 6 || trimmed.Length == hashes || trimmed[hashes] != ' ')
            return null;

        var heading = trimmed[hashes..].Trim();
        return labels.FirstOrDefault(label => string.Equals(label, heading, StringComparison.OrdinalIgnoreCase));
    }
}
