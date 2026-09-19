using System.Text.Json;
using System.Text.RegularExpressions;

namespace Borea.Storage.Listings;

/// <summary>
/// Resolves SPDX license expressions against the SPDX License List that ships with Borea, with the rules of
/// check_license.py of content-index: identifiers compare case-insensitively, and LicenseRef- names a license of your own.
/// </summary>
public sealed partial class SpdxLicenseList
{
    public const string ListUrl = "https://spdx.org/licenses/";

    private static readonly Lazy<SpdxLicenseList> EmbeddedList = new(Load);

    private readonly HashSet<string> _licenses;
    private readonly HashSet<string> _exceptions;

    private SpdxLicenseList(string version, IEnumerable<string> licenses, IEnumerable<string> exceptions)
    {
        Version = version;
        _licenses = new HashSet<string>(licenses, StringComparer.OrdinalIgnoreCase);
        _exceptions = new HashSet<string>(exceptions, StringComparer.OrdinalIgnoreCase);
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

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var parser = new Parser(Tokens(expression), this, unknown);
        if (!parser.Parse())
        {
            return [$"'{expression}' does not parse as an SPDX license expression; join several licenses with AND or OR, "
                + "such as GPL-2.0-only AND CC-BY-SA-4.0"];
        }

        return unknown.Count == 0
            ? []
            : [$"'{expression}' names {string.Join(", ", unknown)}, which is not on the SPDX license list; the identifiers are at {ListUrl}"];
    }

    private static List<string>? Tokens(string expression)
    {
        var tokens = new List<string>();
        foreach (Match match in Token().Matches(expression))
        {
            if (match.Groups["bad"].Success)
                return null;
            tokens.Add(match.Value);
        }

        return tokens;
    }

    private static SpdxLicenseList Load()
    {
        using var stream = typeof(SpdxLicenseList).Assembly.GetManifestResourceStream("Borea.Storage.Listings.spdx-licenses.json")
            ?? throw new InvalidOperationException("The SPDX license list is missing from this build.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new SpdxLicenseList(
            root.GetProperty("licenseListVersion").GetString()!,
            root.GetProperty("licenses").EnumerateArray().Select(item => item.GetString()!),
            root.GetProperty("exceptions").EnumerateArray().Select(item => item.GetString()!));
    }

    /// <summary>expression := and (OR and)*, and := with (AND with)*, with := atom (WITH exception)?, atom := id | ( expression ).</summary>
    private sealed class Parser(List<string>? tokens, SpdxLicenseList list, SortedSet<string> unknown)
    {
        private int _position;

        public bool Parse() => tokens is { Count: > 0 } && Expression() && _position == tokens.Count;

        private bool Expression()
        {
            if (!And())
                return false;
            while (Accept("OR"))
            {
                if (!And())
                    return false;
            }

            return true;
        }

        private bool And()
        {
            if (!With())
                return false;
            while (Accept("AND"))
            {
                if (!With())
                    return false;
            }

            return true;
        }

        private bool With()
        {
            if (Peek() == "(")
            {
                _position++;
                return Expression() && Accept(")");
            }

            if (Identifier() is not { } license)
                return false;
            if (!list.IsLicense(license) && !IsReference(license))
                unknown.Add(license);

            if (!Accept("WITH"))
                return true;

            if (Identifier() is not { } exception)
                return false;
            if (!list._exceptions.Contains(exception) && !IsReference(exception))
                unknown.Add(exception);
            return true;
        }

        private string? Identifier()
        {
            var token = Peek();
            if (token is null or "(" or ")" || IsOperator(token))
                return null;
            _position++;
            return token;
        }

        private bool Accept(string expected)
        {
            if (!string.Equals(Peek(), expected, StringComparison.OrdinalIgnoreCase))
                return false;
            _position++;
            return true;
        }

        private string? Peek() => _position < tokens!.Count ? tokens[_position] : null;

        private static bool IsOperator(string token) =>
            token.Equals("AND", StringComparison.OrdinalIgnoreCase) || token.Equals("OR", StringComparison.OrdinalIgnoreCase) || token.Equals("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReference(string id) => LicenseReference().IsMatch(id);

    [GeneratedRegex(@"[()]|[A-Za-z0-9.:+-]+|(?<bad>[^\s()A-Za-z0-9.:+-])")]
    private static partial Regex Token();

    [GeneratedRegex(@"^(?:DocumentRef-[A-Za-z0-9.-]+:)?LicenseRef-[A-Za-z0-9.-]+$")]
    private static partial Regex LicenseReference();
}
