using Borea.Core.Dependencies;

namespace Borea.Core.Mods
{
    /// <summary>
    /// A version constraint used to express what a <see cref="ModDependency"/> requires,
    /// e.g. ">=1.2.0 &lt;2.0.0". Multiple space-separated clauses are combined with AND.
    /// </summary>
    public sealed class VersionRange
    {
        private readonly IReadOnlyList<Clause> _clauses;

        /// <summary>The original constraint string this range was parsed from.</summary>
        public string Expression { get; }

        private VersionRange(string expression, IReadOnlyList<Clause> clauses)
        {
            Expression = expression;
            _clauses = clauses;
        }

        /// <summary>Returns true if <paramref name="version"/> satisfies every clause in this range.</summary>
        public bool Satisfies(ModVersion version) => _clauses.All(clause => clause.Matches(version));

        /// <summary>
        /// Parses a constraint expression such as "1.2.3", ">=1.2.0", or ">=1.2.0 &lt;2.0.0".
        /// A bare version with no operator is treated as an exact match.
        /// </summary>
        public static VersionRange Parse(string expression)
        {
            if (!TryParse(expression, out var result))
            {
                throw new FormatException($"'{expression}' is not a valid version range.");
            }

            return result;
        }

        public static bool TryParse(string? expression, out VersionRange result)
        {
            result = null!;

            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            var tokens = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var clauses = new List<Clause>(tokens.Length);

            foreach (var token in tokens)
            {
                if (!TryParseClause(token, out var clause))
                {
                    return false;
                }

                clauses.Add(clause);
            }

            if (clauses.Count == 0)
            {
                return false;
            }

            result = new VersionRange(expression, clauses);
            return true;
        }

        private static bool TryParseClause(string token, out Clause clause)
        {
            clause = default;

            var (op, versionPart) = token switch
            {
                _ when token.StartsWith(">=", StringComparison.Ordinal) => (Operator.GreaterThanOrEqual, token[2..]),
                _ when token.StartsWith("<=", StringComparison.Ordinal) => (Operator.LessThanOrEqual, token[2..]),
                _ when token.StartsWith(">", StringComparison.Ordinal) => (Operator.GreaterThan, token[1..]),
                _ when token.StartsWith("<", StringComparison.Ordinal) => (Operator.LessThan, token[1..]),
                _ when token.StartsWith("==", StringComparison.Ordinal) => (Operator.Equal, token[2..]),
                _ when token.StartsWith("=", StringComparison.Ordinal) => (Operator.Equal, token[1..]),
                _ => (Operator.Equal, token),
            };

            if (!ModVersion.TryParse(versionPart, out var version))
            {
                return false;
            }

            clause = new Clause(op, version);
            return true;
        }

        private enum Operator
        {
            Equal,
            GreaterThan,
            GreaterThanOrEqual,
            LessThan,
            LessThanOrEqual,
        }

        private readonly struct Clause
        {
            private readonly Operator _op;
            private readonly ModVersion _version;

            public Clause(Operator op, ModVersion version)
            {
                _op = op;
                _version = version;
            }

            public bool Matches(ModVersion candidate) => _op switch
            {
                Operator.Equal => candidate == _version,
                Operator.GreaterThan => candidate > _version,
                Operator.GreaterThanOrEqual => candidate >= _version,
                Operator.LessThan => candidate < _version,
                Operator.LessThanOrEqual => candidate <= _version,
                _ => throw new InvalidOperationException($"Unhandled operator '{_op}'."),
            };
        }

        public override string ToString() => Expression;
    }
}
