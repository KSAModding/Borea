namespace Borea.Core.Updates;

/// <summary>Reads a checksums listing as the sha256sum program writes it, one "hash  name" line per file.</summary>
public static class Sha256Sums
{
    private const int HashLength = 64;

    /// <summary>
    /// The uppercase hex SHA-256 the listing records for <paramref name="fileName"/>, or null when it
    /// records none. The marker that sha256sum puts in front of a binary-mode name is ignored.
    /// </summary>
    public static string? Find(string? listing, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (string.IsNullOrEmpty(listing))
            return null;

        foreach (var line in listing.Split('\n'))
        {
            var separator = line.IndexOf(' ');
            if (separator != HashLength)
                continue;

            var hash = line[..separator];
            if (!IsHex(hash))
                continue;

            var name = line[(separator + 1)..].TrimEnd('\r').TrimStart(' ', '*');
            if (string.Equals(name, fileName, StringComparison.Ordinal))
                return hash.ToUpperInvariant();
        }

        return null;
    }

    private static bool IsHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
                return false;
        }

        return true;
    }
}
