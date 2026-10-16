using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Borea.Core.Licenses;

namespace Borea.Storage.Listings;

/// <summary>
/// Resolves SPDX license expressions against the SPDX License List that ships with Borea, with the rules of
/// check_license.py of content-index: identifiers compare case-insensitively, and LicenseRef- names a license of your own.
/// </summary>
public sealed partial class SpdxLicenseList
{
    public const string ListUrl = "https://spdx.org/licenses/";

    /// <summary>How many close ids a message names for an id that is not on the list.</summary>
    private const int CloseLimit = 6;

    private static readonly Lazy<SpdxLicenseList> EmbeddedList = new(Load);

    private readonly HashSet<string> _licenses;
    private readonly HashSet<string> _exceptions;
    private readonly List<string> _closeLicenses;
    private readonly List<string> _closeExceptions;

    /// <summary>
    /// The close ids come from lists of their own, the ids that content-index names as close ones, so that a message
    /// names the same ids as the index.
    /// </summary>
    private SpdxLicenseList(string version, List<string> licenses, List<string> exceptions, List<string> closeLicenses, List<string> closeExceptions)
    {
        Version = version;
        _licenses = new HashSet<string>(licenses, StringComparer.OrdinalIgnoreCase);
        _exceptions = new HashSet<string>(exceptions, StringComparer.OrdinalIgnoreCase);
        _closeLicenses = closeLicenses;
        _closeExceptions = closeExceptions;
    }

    public static SpdxLicenseList Embedded => EmbeddedList.Value;

    /// <summary>The version of the SPDX License List, such as 3.29.0.</summary>
    public string Version { get; }

    public bool IsLicense(string id) => _licenses.Contains(id);

    /// <summary>What is wrong with the expression, in the words of the checks. Empty when nothing is.</summary>
    public IReadOnlyList<string> Problems(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Trim().Length == 0)
            return [];

        var depth = 0;
        foreach (var character in expression)
        {
            depth += character switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth < 0)
                break;
        }

        if (depth != 0)
            return [$"'{expression}' has unbalanced parentheses"];

        if (SpdxExpression.Parse(expression) is not { } parsed)
        {
            return [$"'{expression}' does not parse as an SPDX license expression; join several licenses with AND or OR, "
                + "such as GPL-2.0-only AND CC-BY-SA-4.0"];
        }

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var license in parsed.Licenses)
        {
            if (!IsLicense(license.Id) && !IsReference(license.Id))
                unknown.Add(license.Id);
            if (license.Exception is { } exception && !_exceptions.Contains(exception) && !IsReference(exception))
                unknown.Add(exception);
        }

        return unknown.Count == 0
            ? []
            : [$"'{expression}' names {string.Join(", ", unknown)}, which is not on the SPDX license list{CloseText(expression, unknown)}; the identifiers are at {ListUrl}"];
    }

    /// <summary>
    /// The ids of <paramref name="ids"/> that start like <paramref name="name"/>, as check_license.py of content-index
    /// finds them. An id that is the name itself comes first, then the ids that continue with a version, then the
    /// shorter ones. When no id starts with the whole name, the letters at its start are tried without a v in front
    /// of a version, so GPLv3 finds GPL-3.0-only.
    /// </summary>
    private static IReadOnlyList<string> CloseIds(string name, IReadOnlyCollection<string> ids)
    {
        var letters = LeadingLetters().Match(name).Value;
        if (letters.EndsWith('v') || letters.EndsWith('V'))
        {
            if (name.Length > letters.Length && char.IsAsciiDigit(name[letters.Length]))
                letters = letters[..^1];
        }

        foreach (var stem in new[] { name, letters })
        {
            if (stem.Length == 0)
                continue;

            var folded = stem.ToLowerInvariant();
            var prefix = folded + "-";
            var found = ids
                .Where(id => id.Equals(folded, StringComparison.OrdinalIgnoreCase) || id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(id => id.Equals(folded, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(id => id.Length > prefix.Length && char.IsAsciiDigit(id[prefix.Length]) ? 0 : 1)
                .ThenBy(id => id.Length)
                .ThenBy(id => id.ToLowerInvariant(), StringComparer.Ordinal)
                .ThenBy(id => id, StringComparer.Ordinal)
                .Take(CloseLimit)
                .ToList();
            if (found.Count > 0)
                return found;
        }

        return [];
    }

    /// <summary>
    /// The close ids of each name that is on neither list, as clauses of the message. A name after WITH gets close
    /// exception ids and any other name gets close license ids.
    /// </summary>
    private string CloseText(string expression, IEnumerable<string> names)
    {
        var text = new StringBuilder();
        foreach (var name in names.Where(name => !IsLicense(name) && !_exceptions.Contains(name)))
        {
            var found = CloseIds(name, AfterWith(expression, name) ? _closeExceptions : _closeLicenses);
            if (found.Count == 1)
                text.Append(CultureInfo.InvariantCulture, $"; the id close to {name} is {found[0]}");
            else if (found.Count > 1)
                text.Append(CultureInfo.InvariantCulture, $"; ids close to {name} are {string.Join(", ", found.Take(found.Count - 1))} and {found[^1]}");
        }

        return text.ToString();
    }

    private static bool AfterWith(string expression, string name) =>
        Regex.IsMatch(expression, $@"(?<![^\s()])WITH\s+{Regex.Escape(name)}(?![^\s()])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static SpdxLicenseList Load()
    {
        using var stream = typeof(SpdxLicenseList).Assembly.GetManifestResourceStream("Borea.Storage.Listings.spdx-licenses.json")
            ?? throw new InvalidOperationException("The SPDX license list is missing from this build.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new SpdxLicenseList(
            root.GetProperty("licenseListVersion").GetString()!,
            Ids(root, "licenses").ToList(),
            Ids(root, "exceptions").ToList(),
            Ids(root, "closeLicenses").ToList(),
            Ids(root, "closeExceptions").ToList());
    }

    private static IEnumerable<string> Ids(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(item => item.GetString()!);

    private static bool IsReference(string id) => LicenseReference().IsMatch(id);

    [GeneratedRegex(@"^(?:DocumentRef-[A-Za-z0-9.-]+:)?LicenseRef-[A-Za-z0-9.-]+$")]
    private static partial Regex LicenseReference();

    [GeneratedRegex("^[A-Za-z]*")]
    private static partial Regex LeadingLetters();
}
