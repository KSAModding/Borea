namespace Borea.Core.Listings;

/// <summary>The listing id the client proposes for a shared vehicle or save, from the name in its meta.toml (RFC 0085).</summary>
public static class ListingShareId
{
    public const int MaxLength = 64;

    /// <summary>The ASCII letters and digits of the name, at most <see cref="MaxLength"/>. Empty when the name has none.</summary>
    public static string From(string? name)
    {
        var letters = (name ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Take(MaxLength).ToArray();
        return new string(letters);
    }
}
