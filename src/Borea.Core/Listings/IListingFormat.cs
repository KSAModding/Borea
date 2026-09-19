namespace Borea.Core.Listings;

/// <summary>Reads and writes authored documents in the TOML layout of the listings in content-index.</summary>
public interface IListingFormat
{
    /// <summary>The same document always gives the same text. Given the listed file, the description keeps the quotes of that file.</summary>
    string Write(AuthoredTable document, string? original = null);

    /// <exception cref="FormatException">The text is not a TOML document.</exception>
    AuthoredTable Read(string text);
}
