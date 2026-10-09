using System.Numerics;
using System.Text;

namespace Borea.Storage.Listings;

/// <summary>
/// The sort key that semver_key of check_schema.py gives a version bound. It reads one to three decimal numbers of any size,
/// with leading zeros and with the decimal digits of every script, and a missing number is 0. Build metadata after "+" does
/// not count, and a release ranks above each of its pre-releases. A numeric pre-release identifier ranks below a text one.
/// </summary>
internal sealed class ListingVersionKey : IComparable<ListingVersionKey>
{
    private readonly BigInteger[] _core;

    /// <summary>The pre-release identifiers, or null for a release.</summary>
    private readonly Identifier[]? _pre;

    private ListingVersionKey(BigInteger[] core, Identifier[]? pre)
    {
        _core = core;
        _pre = pre;
    }

    /// <summary>The key of a version, or null when check_schema.py cannot read it, so the bounds are not compared.</summary>
    public static ListingVersionKey? Of(string? version)
    {
        if (version is null)
            return null;

        var withoutBuild = version.Split('+', 2)[0];
        var parts = withoutBuild.Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length is < 1 or > 3 || !numbers.All(IsDecimal))
            return null;

        var core = new BigInteger[3];
        for (var index = 0; index < numbers.Length; index++)
            core[index] = Decimal(numbers[index]);

        var pre = parts.Length < 2 || parts[1].Length == 0
            ? null
            : parts[1].Split('.').Select(part => IsDecimal(part) ? new Identifier(Decimal(part), null) : new Identifier(BigInteger.Zero, part)).ToArray();
        return new ListingVersionKey(core, pre);
    }

    public int CompareTo(ListingVersionKey? other)
    {
        if (other is null)
            return 1;

        for (var index = 0; index < _core.Length; index++)
        {
            var order = _core[index].CompareTo(other._core[index]);
            if (order != 0)
                return order;
        }

        if (_pre is null || other._pre is null)
            return (_pre is null ? 1 : 0).CompareTo(other._pre is null ? 1 : 0);

        for (var index = 0; index < Math.Min(_pre.Length, other._pre.Length); index++)
        {
            var order = _pre[index].CompareTo(other._pre[index]);
            if (order != 0)
                return order;
        }

        return _pre.Length.CompareTo(other._pre.Length);
    }

    /// <summary>Whether the text is one or more decimal digits, as str.isdecimal of Python reads it.</summary>
    private static bool IsDecimal(string text) => text.Length > 0 && text.EnumerateRunes().All(Rune.IsDigit);

    private static BigInteger Decimal(string text)
    {
        var value = BigInteger.Zero;
        foreach (var rune in text.EnumerateRunes())
            value = (value * 10) + (int)Rune.GetNumericValue(rune);
        return value;
    }

    /// <param name="Text">The identifier when it is not a number, or null for a number.</param>
    private readonly record struct Identifier(BigInteger Number, string? Text) : IComparable<Identifier>
    {
        public int CompareTo(Identifier other)
        {
            if (Text is null || other.Text is null)
            {
                var kind = (Text is null ? 0 : 1).CompareTo(other.Text is null ? 0 : 1);
                return kind != 0 ? kind : Number.CompareTo(other.Number);
            }

            return CompareCodePoints(Text, other.Text);
        }

        /// <summary>Compares by Unicode code point, as Python compares strings.</summary>
        private static int CompareCodePoints(string left, string right)
        {
            using var first = left.EnumerateRunes().GetEnumerator();
            using var second = right.EnumerateRunes().GetEnumerator();
            while (true)
            {
                var hasFirst = first.MoveNext();
                var hasSecond = second.MoveNext();
                if (!hasFirst || !hasSecond)
                    return hasFirst.CompareTo(hasSecond);

                var order = first.Current.Value.CompareTo(second.Current.Value);
                if (order != 0)
                    return order;
            }
        }
    }
}
