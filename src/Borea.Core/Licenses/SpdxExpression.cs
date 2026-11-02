using System.Text.RegularExpressions;

namespace Borea.Core.Licenses;

/// <summary>
/// An SPDX license expression, read with the grammar expression := and (OR and)*, and := with (AND with)*,
/// with := atom (WITH exception)?, atom := id | ( expression ). Operators compare case-insensitively.
/// </summary>
public sealed partial class SpdxExpression
{
    /// <summary>How deep parentheses can go. The parser recurses once per level, so a deeper text does not parse.</summary>
    public const int MaxDepth = 64;

    private readonly Node _root;

    private SpdxExpression(Node root)
    {
        _root = root;
        Licenses = root.Licenses().Distinct().ToList();
    }

    /// <summary>
    /// Every license that the expression names, in the order of the expression. A license written twice with the same
    /// spelling is here once, and a license written twice with different case is here twice.
    /// </summary>
    public IReadOnlyList<SpdxLicense> Licenses { get; }

    /// <summary>Turns the expression into one value. Each license gives a value, and each AND and each OR joins the values of its parts.</summary>
    public T Fold<T>(Func<SpdxLicense, T> ofLicense, Func<IEnumerable<T>, T> and, Func<IEnumerable<T>, T> or)
    {
        ArgumentNullException.ThrowIfNull(ofLicense);
        ArgumentNullException.ThrowIfNull(and);
        ArgumentNullException.ThrowIfNull(or);
        return _root.Fold(ofLicense, and, or);
    }

    /// <summary>The expression, or null when the text does not parse or its parentheses go deeper than <see cref="MaxDepth"/>.</summary>
    public static SpdxExpression? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Tokens(text) is { Count: > 0 } tokens && new Parser(tokens).Parse() is { } root ? new SpdxExpression(root) : null;
    }

    private static List<string>? Tokens(string text)
    {
        var tokens = new List<string>();
        foreach (Match match in Token().Matches(text))
        {
            if (match.Groups["bad"].Success)
                return null;
            tokens.Add(match.Value);
        }

        return tokens;
    }

    private abstract class Node
    {
        public abstract IEnumerable<SpdxLicense> Licenses();

        public abstract T Fold<T>(Func<SpdxLicense, T> ofLicense, Func<IEnumerable<T>, T> and, Func<IEnumerable<T>, T> or);
    }

    private sealed class LicenseNode(SpdxLicense license) : Node
    {
        public override IEnumerable<SpdxLicense> Licenses() => [license];

        public override T Fold<T>(Func<SpdxLicense, T> ofLicense, Func<IEnumerable<T>, T> and, Func<IEnumerable<T>, T> or) => ofLicense(license);
    }

    private sealed class AndNode(List<Node> parts) : Node
    {
        public override IEnumerable<SpdxLicense> Licenses() => parts.SelectMany(part => part.Licenses());

        public override T Fold<T>(Func<SpdxLicense, T> ofLicense, Func<IEnumerable<T>, T> and, Func<IEnumerable<T>, T> or) =>
            and(parts.Select(part => part.Fold(ofLicense, and, or)));
    }

    private sealed class OrNode(List<Node> parts) : Node
    {
        public override IEnumerable<SpdxLicense> Licenses() => parts.SelectMany(part => part.Licenses());

        public override T Fold<T>(Func<SpdxLicense, T> ofLicense, Func<IEnumerable<T>, T> and, Func<IEnumerable<T>, T> or) =>
            or(parts.Select(part => part.Fold(ofLicense, and, or)));
    }

    private sealed class Parser(List<string> tokens)
    {
        private int _position;
        private int _depth;

        public Node? Parse() => Expression() is { } root && _position == tokens.Count ? root : null;

        private Node? Expression() => Sequence("OR", And, parts => new OrNode(parts));

        private Node? And() => Sequence("AND", With, parts => new AndNode(parts));

        private Node? Sequence(string separator, Func<Node?> part, Func<List<Node>, Node> join)
        {
            var parts = new List<Node>();
            do
            {
                if (part() is not { } node)
                    return null;
                parts.Add(node);
            }
            while (Accept(separator));

            return parts.Count == 1 ? parts[0] : join(parts);
        }

        private Node? With()
        {
            if (Peek() == "(")
            {
                if (_depth == MaxDepth)
                    return null;
                _position++;
                _depth++;
                var inner = Expression();
                _depth--;
                return inner is not null && Accept(")") ? inner : null;
            }

            if (Identifier() is not { } license)
                return null;
            if (!Accept("WITH"))
                return new LicenseNode(new SpdxLicense(license, null));

            return Identifier() is { } exception ? new LicenseNode(new SpdxLicense(license, exception)) : null;
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

        private string? Peek() => _position < tokens.Count ? tokens[_position] : null;

        private static bool IsOperator(string token) =>
            token.Equals("AND", StringComparison.OrdinalIgnoreCase) || token.Equals("OR", StringComparison.OrdinalIgnoreCase) || token.Equals("WITH", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[()]|[A-Za-z0-9.:+-]+|(?<bad>[^\s()A-Za-z0-9.:+-])")]
    private static partial Regex Token();
}
